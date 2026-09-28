using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Native Unity cloth for the three town-service stations.
/// The original visible mesh and materials remain in place; an invisible
/// <see cref="Cloth"/> driver supplies their simulated vertices. This is the
/// same PhysX cloth and tapered hand collider mechanism used by figure
/// garments, extended to the glove's wrist and fitted to the real table lip.</summary>
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
    // The video at ModBuild 578 shows the back of the glove crossing the red
    // enchantress drape while the original palm-to-index capsule is outside it.
    // Extend the same tapered capsule to the wrist. At the real palm its
    // interpolated radius is still ~35 mm; at the cuff it covers the visible
    // hand instead of allowing that half of the glove to pass through the sheet.
    private const float WristRadiusRealMeters = PalmRadiusRealMeters + .020f;
    private const float MaximumFreedomRealMeters = .08f;
    private const float ContactPresentationSeconds = .045f;
    // The fingertip and the PhysX sheet can alternate sides of a thin surface
    // between two 90 Hz presentation ticks. A contact episode must not fade out
    // during that solver crossing while the hand remains in its approach volume.
    private const float ContactHoldSeconds = .09f;
    // Unity 2021.3 refuses to initialize a Cloth whose *every* particle starts
    // fixed. The headset log then says "All cloth particles are fixed so the
    // Cloth component is not initialized" and every touch remains rigid even
    // after SetFreedom expands the coefficients. Keep a sub-millimetre live
    // envelope while idle so PhysX cooks the solver on its first enabled frame.
    private const float IdleFreedomRealMeters = .0005f;
    private const float HandClearanceRealMeters = .016f;
    private const float ClearanceSpreadRealMeters = .17f;
    private const float ClearanceLimitRealMeters = .22f;

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
        internal Renderer? VisibleRenderer;
        internal Mesh VisibleMesh = null!;
        internal Vector3[] Rest = Array.Empty<Vector3>();
        internal Vector3[] Shown = Array.Empty<Vector3>();
        internal float[] Freedom = Array.Empty<float>();
        internal float[] Side = Array.Empty<float>();
        internal float[] Clearance = Array.Empty<float>();
        internal readonly int[] ProbeSide = new int[MaximumHands];
        internal readonly ProjectionProbe[] Projection = new ProjectionProbe[MaximumHands];
        internal int ProjectionCount;
        internal bool ActiveClearance;
        internal int[] VisibleDriverVertex = Array.Empty<int>();
        internal Vector3[] DriverRest = Array.Empty<Vector3>();
        internal Vector3[] VisualDelta = Array.Empty<Vector3>();
        internal Vector3[] VisualScratch = Array.Empty<Vector3>();
        // Last PhysX surface sampled by Render. Contact has to follow this surface,
        // not the authored zero: once a fingertip has pushed the runner a few
        // centimetres, testing against DriverRest declares that same fingertip
        // "gone" and fades the visible deformation while it is still touching.
        internal Vector3[] ContactSurface = Array.Empty<Vector3>();
        internal Vector3[] EpisodeOrigin = Array.Empty<Vector3>();
        internal float[] DriverFreedom = Array.Empty<float>();
        internal float[] DriverSide = Array.Empty<float>();
        internal float[] DriverMaximum = Array.Empty<float>();
        internal DriverMap[] VisibleDriverMap = Array.Empty<DriverMap>();
        internal GameObject DriverRoot = null!;
        internal Mesh DriverMesh = null!;
        internal Cloth Cloth = null!;
        internal float DriverBaseScale;
        internal float StationUnitInDriver;
        internal readonly List<Decoration> Decorations = new();
        internal readonly List<GameObject> Supports = new();
        internal readonly List<ClothSphereColliderPair> SupportPairs = new();
        internal TownClothRunnerState State;
        internal float NextRender;
        internal float DeformationWeight;
        internal bool Interactive;
        internal bool Contacting;
        internal float ContactHold;
        internal int DebugSuppressedContactGaps;
        internal bool DebugNear;
        internal bool DebugContact;
        internal bool DebugReady;
        internal float DebugShownPeak;
    }

    private struct DriverMap
    {
        internal int A, B, C, D;
        internal Vector4 Weight;
    }

    private struct ProjectionProbe
    {
        internal Vector3 Wrist;
        internal Vector3 Tip;
        internal float WristRadius;
        internal float TipRadius;
        internal int Side;
    }

    private sealed class HandProbe
    {
        internal GameObject Root = null!;
        internal Transform Palm = null!;
        internal Transform Tip = null!;
        internal SphereCollider PalmSphere = null!;
        internal SphereCollider TipSphere = null!;
        internal ClothSphereColliderPair Pair;
        internal Vector3 PreviousPalm;
        internal Vector3 PreviousTip;
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
        // The merchant's experimental side drape is no longer part of the cabinet.
        // Keep the two temple runners and the enchantress's runner physical.
        if (filters.Count != (service == 1 ? 0 : service == 2 ? 2 : 1))
            throw new InvalidOperationException("Town cloth mesh count does not match the authored furniture for service " + service);
        if (filters.Count == 0) return;

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
            VisibleRenderer = filter.GetComponent<Renderer>(),
            VisibleMesh = visible,
            Rest = rest,
            Shown = new Vector3[rest.Length],
            Freedom = new float[rest.Length],
            Side = new float[rest.Length],
            Clearance = new float[rest.Length],
            VisibleDriverVertex = new int[rest.Length],
            VisibleDriverMap = new DriverMap[rest.Length]
        };
        Array.Copy(rest, runner.Shown, rest.Length);

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int n = 0; n < rest.Length; n++)
        {
            Vector3 p = _station.InverseTransformPoint(filter.transform.TransformPoint(rest[n]));
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
        }
        if (maxY - minY < .3f || (_service == 1 ? maxZ - minZ : maxX - minX) < .1f)
            throw new InvalidOperationException("Town cloth dimensions do not match the native cloth source.");
        for (int n = 0; n < rest.Length; n++)
        {
            Vector3 p = _station.InverseTransformPoint(filter.transform.TransformPoint(rest[n]));
            runner.Freedom[n] = Freedom(p, maxY);
            runner.Side[n] = _service == 1
                ? Mathf.Clamp01((p.z - minZ) / (maxZ - minZ))
                : Mathf.Clamp01((p.x - minX) / (maxX - minX));
        }

        BuildNativeDriver(runner, minX, maxX);
        BuildDecorations(runner);
        if (_service == 1) BuildMerchantSupports(runner);
        else BuildTableSupports(runner, minX, maxX, maxY);
        if (_service == 3) BuildEnchantressRootSupport(runner);
        RewriteColliders(runner);
        return runner;
    }

    private void BuildNativeDriver(Runner runner, float minX, float maxX)
    {
        runner.DriverRoot = new GameObject("GloomhavenVR.NativeTownCloth") { layer = IgnoreRaycastLayer };
        Transform driver = runner.DriverRoot.transform;
        // Unity Cloth becomes numerically unstable when the solver itself inherits
        // the town hierarchy's ~19,800x lossy scale. Keep the solver at unit scale,
        // bake the already-placed sheet into that space, and follow only the source
        // mesh's world pose. A ratio near one handles a later room-scale change.
        driver.SetPositionAndRotation(runner.Filter.transform.position, runner.Filter.transform.rotation);
        driver.localScale = Vector3.one;
        runner.DriverBaseScale = UniformScale(runner.Filter.transform);

        // Blender's Solidify output contains a second surface and side walls.
        // Feeding that closed shell to PhysX makes two nearly coincident sheets
        // fight each other and produces rigid board-like motion. Do not assume
        // the first 325 imported vertices are Blender's source grid either: the
        // shipping FBX importer reorders and splits them by face/UV (889-918
        // vertices, with the first thirteen running DOWN one column). Build the
        // authored 25x13 surface from its measured curved-lip contract, then map
        // every rendered thickness vertex to that physical sheet. A proximity
        // check below makes any future furniture-authoring change fail closed.
        runner.DriverRest = new Vector3[DriverVertexCount];
        runner.VisualDelta = new Vector3[DriverVertexCount];
        runner.VisualScratch = new Vector3[DriverVertexCount];
        runner.EpisodeOrigin = new Vector3[DriverVertexCount];
        for (int row = 0; row < DriverRows; row++)
        for (int column = 0; column < DriverColumns; column++)
        {
            Vector3 stationPoint = _service == 1 ? MerchantAuthoredPoint(row, column)
                : AuthoredPoint(row, column, minX, maxX);
            float closest = float.MaxValue;
            for (int visible = 0; visible < runner.Rest.Length; visible++)
            {
                Vector3 candidate = _station.InverseTransformPoint(
                    runner.Filter.transform.TransformPoint(runner.Rest[visible]));
                closest = Mathf.Min(closest, (candidate - stationPoint).sqrMagnitude);
            }
            if (closest > .008f * .008f)
                throw new InvalidOperationException("Town cloth rendered mesh no longer matches its physical source grid.");
            runner.DriverRest[row * DriverColumns + column] = driver.InverseTransformPoint(
                _station.TransformPoint(stationPoint));
        }
        Array.Copy(runner.DriverRest, runner.EpisodeOrigin, runner.DriverRest.Length);
        runner.ContactSurface = runner.DriverRest;
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
        // The driver deliberately lives at world scale one. Native gravity is
        // expressed in world units, while the map uses roughly 198 world units
        // for one perceived metre. `useGravity=true` therefore supplied only
        // about 0.05 g to this solver: after a finger left, the runner stayed in
        // its crumpled pose for seconds. Apply one physical g in station metres
        // explicitly once the station-to-driver conversion is known below.

        // Cloth coefficients use the driver's particle space. The first native
        // implementation treated them as world units and multiplied by the live
        // 19800x lossy scale (roughly 198 map scale x Blender's 100x FBX child).
        // It gave a sheet whose local width is about 0.012 a maxDistance in the
        // THOUSANDS and a collision skin tens of world units thick. The solver
        // could neither conform to the worktop nor wait for a physical finger to
        // touch it. Convert the intended station-space distances into the mesh's
        // local particle units exactly once. FigureCloth's coefficient rescale is
        // for an already-cooked garment whose miniature changes scale afterwards;
        // this cloth is created only after its final station hierarchy is placed.
        float physicalMinX = float.MaxValue, physicalMaxX = float.MinValue;
        float physicalMinZ = float.MaxValue, physicalMaxZ = float.MinValue, maxY = float.MinValue;
        runner.DriverFreedom = new float[runner.DriverRest.Length];
        runner.DriverSide = new float[runner.DriverRest.Length];
        runner.DriverMaximum = new float[runner.DriverRest.Length];
        for (int n = 0; n < runner.DriverRest.Length; n++)
        {
            Vector3 p = _station.InverseTransformPoint(driver.TransformPoint(runner.DriverRest[n]));
            physicalMinX = Mathf.Min(physicalMinX, p.x);
            physicalMaxX = Mathf.Max(physicalMaxX, p.x); maxY = Mathf.Max(maxY, p.y);
            physicalMinZ = Mathf.Min(physicalMinZ, p.z); physicalMaxZ = Mathf.Max(physicalMaxZ, p.z);
        }
        for (int n = 0; n < runner.DriverRest.Length; n++)
        {
            Vector3 p = _station.InverseTransformPoint(driver.TransformPoint(runner.DriverRest[n]));
            runner.DriverFreedom[n] = Freedom(p, maxY);
            runner.DriverSide[n] = _service == 1
                ? Mathf.Clamp01((p.z - physicalMinZ) / Mathf.Max(.0001f, physicalMaxZ - physicalMinZ))
                : Mathf.Clamp01((p.x - physicalMinX) / Mathf.Max(.0001f, physicalMaxX - physicalMinX));
        }
        for (int n = 0; n < runner.Rest.Length; n++)
        {
            Vector3 point = runner.Filter.transform.TransformPoint(runner.Rest[n]);
            runner.VisibleDriverVertex[n] = Nearest(point, driver, runner.DriverRest);
            runner.VisibleDriverMap[n] = Map(point, driver, runner.DriverRest);
        }

        float stationUnitInDriver = driver.InverseTransformVector(
            _station.TransformVector(Vector3.up)).magnitude;
        if (stationUnitInDriver < .000001f || float.IsNaN(stationUnitInDriver)
            || float.IsInfinity(stationUnitInDriver))
            throw new InvalidOperationException("Town cloth station-to-driver scale is invalid.");
        runner.StationUnitInDriver = stationUnitInDriver;
        Configure(cloth, stationUnitInDriver);
        var coefficients = new ClothSkinningCoefficient[runner.DriverRest.Length];
        for (int n = 0; n < coefficients.Length; n++)
        {
            // The original 34 cm envelope let one fingertip invert nearly the
            // complete runner. Build 574's 11 cm envelope remained too loose in
            // the headset: contact rapidly buckled several rows into sharp
            // wrinkles. The root-supported enchantress drape has less room to
            // move than the broad altar cloth; keep its envelope correspondingly
            // shorter while retaining a tactile impression under the fingertip.
            float maximumFreedom = _service == 3 ? .065f : MaximumFreedomRealMeters;
            runner.DriverMaximum[n] = runner.DriverFreedom[n]
                * maximumFreedom * stationUnitInDriver;
            // The authored mesh is already the exact rest drape. Start within a
            // sub-millimetre envelope, then expand for a hand/head approach.
            // An exact zero for every vertex leaves Unity's Cloth uninitialized.
            coefficients[n].maxDistance = runner.DriverFreedom[n]
                * IdleFreedomRealMeters * stationUnitInDriver;
            coefficients[n].collisionSphereDistance = .004f * stationUnitInDriver;
        }
        cloth.coefficients = coefficients;
        cloth.ClearTransformMotion();
        runner.Cloth = cloth;
    }

    private static void Configure(Cloth cloth, float stationUnitInDriver)
    {
        cloth.useGravity = false;
        cloth.useTethers = true;
        // The authored mesh already contains its resting drape. Keep gravity quiet
        // while pinned or merely approached; one physical g is enabled during real
        // contact and its visible recovery below.
        cloth.externalAcceleration = Vector3.zero;
        cloth.damping = .55f;
        cloth.friction = .32f;
        cloth.bendingStiffness = .94f;
        cloth.stretchingStiffness = .96f;
        // Match figure cloth's bounded high-quality rate. Raising this to 180 Hz
        // adds fifty percent solver work for three permanent runners without
        // improving the visible return authored below.
        cloth.clothSolverFrequency = 120f;
        cloth.enableContinuousCollision = true;
        cloth.worldVelocityScale = .35f;
        cloth.worldAccelerationScale = .35f;
        cloth.sleepThreshold = .05f;
    }

    private static float UniformScale(Transform transform)
    {
        Vector3 scale = transform.lossyScale;
        float smallest = Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        float largest = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        if (smallest < .000001f || largest / smallest > 1.01f)
            throw new InvalidOperationException("Town cloth requires a positive uniform furniture scale.");
        return (smallest + largest) * .5f;
    }

    private static void FollowSource(Runner runner)
    {
        Transform source = runner.Filter.transform;
        Transform driver = runner.DriverRoot.transform;
        float ratio = UniformScale(source) / runner.DriverBaseScale;
        Vector3 scale = Vector3.one * ratio;
        if ((driver.position - source.position).sqrMagnitude < .000001f
            && Quaternion.Angle(driver.rotation, source.rotation) < .001f
            && (driver.localScale - scale).sqrMagnitude < .000001f) return;
        driver.SetPositionAndRotation(source.position, source.rotation);
        driver.localScale = scale;
        runner.Cloth.ClearTransformMotion();
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

    private static DriverMap Map(Vector3 worldPoint, Transform driverTransform, Vector3[] vertices)
    {
        int a = 0, b = 0, c = 0, d = 0;
        float da = float.MaxValue, db = float.MaxValue, dc = float.MaxValue, dd = float.MaxValue;
        for (int i = 0; i < vertices.Length; i++)
        {
            float distance = (driverTransform.TransformPoint(vertices[i]) - worldPoint).sqrMagnitude;
            if (distance < da) { dd = dc; d = c; dc = db; c = b; db = da; b = a; da = distance; a = i; }
            else if (distance < db) { dd = dc; d = c; dc = db; c = b; db = distance; b = i; }
            else if (distance < dc) { dd = dc; d = c; dc = distance; c = i; }
            else if (distance < dd) { dd = distance; d = i; }
        }
        if (da < 1e-10f) return new DriverMap { A = a, B = b, C = c, D = d,
            Weight = new Vector4(1f, 0f, 0f, 0f) };
        var inverse = new Vector4(1f / da, 1f / db, 1f / dc, 1f / dd);
        float sum = inverse.x + inverse.y + inverse.z + inverse.w;
        return new DriverMap { A = a, B = b, C = c, D = d, Weight = inverse / sum };
    }

    private static Vector3 BlendedDelta(in DriverMap map, Vector3[] delta)
    {
        return delta[map.A] * map.Weight.x + delta[map.B] * map.Weight.y
            + delta[map.C] * map.Weight.z + delta[map.D] * map.Weight.w;
    }

    private static void SmoothNarrowDrape(Runner runner, Vector3[] simulated)
    {
        // PhysX's sparse 25x13 surface has much wider triangles than the
        // rendered solidified cloth. A fingertip against the narrow root could
        // invert a single triangle, opening a dark pinhole in the visible
        // sheet. Spatially filter only the presentation displacement, leaving
        // native collision/contact and the table-pinned row untouched.
        for (int n = 0; n < simulated.Length; n++)
            runner.VisualDelta[n] = simulated[n] - runner.EpisodeOrigin[n];
        for (int pass = 0; pass < 2; pass++)
        {
            Vector3[] from = pass == 0 ? runner.VisualDelta : runner.VisualScratch;
            Vector3[] to = pass == 0 ? runner.VisualScratch : runner.VisualDelta;
            for (int row = 0; row < DriverRows; row++)
            for (int column = 0; column < DriverColumns; column++)
            {
                int n = row * DriverColumns + column;
                if (row <= 10) { to[n] = from[n]; continue; }
                Vector3 sum = from[n] * .4f;
                float weight = .4f;
                if (row > 0) { sum += from[n - DriverColumns] * .15f; weight += .15f; }
                if (row + 1 < DriverRows) { sum += from[n + DriverColumns] * .15f; weight += .15f; }
                if (column > 0) { sum += from[n - 1] * .15f; weight += .15f; }
                if (column + 1 < DriverColumns) { sum += from[n + 1] * .15f; weight += .15f; }
                to[n] = sum / weight;
            }
        }
    }

    private void BuildTableSupports(Runner runner, float minX, float maxX, float topY)
    {
        // Unity Cloth intentionally collides only with spheres/capsules. A chain
        // following the measured elliptical front edge is therefore the native
        // equivalent of the table's solid lip. It prevents an inward finger push
        // from carrying the runner through the stone/wood while leaving its free
        // lower edge fully movable.
        // Unity Cloth has no mesh-collider input. Support each of the authored
        // source grid's thirteen columns with a front-to-rear capsule instead.
        // Every simulated particle on the tabletop then sits over its own solid
        // support rather than between two sparse horizontal rails. The endpoint
        // sphere follows the curved lip, so the hanging rows remain free while a
        // finger cannot push their seam through the table. Thirteen table pairs
        // plus eight hand and four head pairs remain below the 32-pair limit.
        const int samples = DriverColumns;
        for (int i = 0; i < samples; i++)
        {
            float x = Mathf.Lerp(minX, maxX, i / (samples - 1f));
            SphereCollider front = TableSupport(runner, i, "Front", x, topY, .008f);
            SphereCollider rear = TableSupport(runner, i, "Rear", x, topY, .142f);
            runner.SupportPairs.Add(new ClothSphereColliderPair(front, rear));
        }
    }

    private void BuildEnchantressRootSupport(Runner runner)
    {
        // The narrow red runner hangs immediately in front of the enchantress's
        // left woven root (authored around x=-.62, z=-.24). The table-top
        // capsules stop at the lip: they cannot stop a fingertip pressing the
        // lower hanging rows straight through that root. A single vertical
        // capsule follows the root's actual height and breadth. Its near face
        // remains behind the authored drape, leaving the visible resting sheet
        // undisturbed while giving the moving cloth a solid backstop.
        SphereCollider top = RootSupport(runner, "Top", .84f);
        SphereCollider bottom = RootSupport(runner, "Bottom", .20f);
        runner.SupportPairs.Add(new ClothSphereColliderPair(top, bottom));
    }

    private SphereCollider RootSupport(Runner runner, string end, float y)
    {
        var go = new GameObject("GloomhavenVR.TownCloth.EnchantressRoot." + end)
            { layer = IgnoreRaycastLayer };
        Transform driver = runner.DriverRoot.transform;
        go.transform.SetParent(driver, false);
        go.transform.localPosition = driver.InverseTransformPoint(_station.TransformPoint(
            new Vector3(-.62f, y, -.20f)));
        var sphere = go.AddComponent<SphereCollider>();
        sphere.radius = .075f * runner.StationUnitInDriver;
        runner.Supports.Add(go);
        return sphere;
    }

    private void BuildMerchantSupports(Runner runner)
    {
        // The merchant's side hanging is vertical. A capsule in every grid column
        // follows the cabinet wall behind it, leaving the outer face accessible to
        // fingers while preventing the cloth from passing through the cabinet.
        for (int column = 0; column < DriverColumns; column++)
        {
            float z = Mathf.Lerp(.205f, .460f, column / (DriverColumns - 1f));
            SphereCollider top = MerchantSupport(runner, column, "Top", 1.57f, z);
            SphereCollider bottom = MerchantSupport(runner, column, "Bottom", 1.00f, z);
            runner.SupportPairs.Add(new ClothSphereColliderPair(top, bottom));
        }
    }

    private SphereCollider MerchantSupport(Runner runner, int column, string end, float y, float z)
    {
        var go = new GameObject("GloomhavenVR.TownCloth.MerchantSupport." + column + "." + end)
            { layer = IgnoreRaycastLayer };
        Transform driver = runner.DriverRoot.transform;
        go.transform.SetParent(driver, false);
        go.transform.localPosition = driver.InverseTransformPoint(_station.TransformPoint(
            new Vector3(-1.334f, y, z)));
        var sphere = go.AddComponent<SphereCollider>();
        sphere.radius = .041f * driver.InverseTransformVector(
            _station.TransformVector(Vector3.up)).magnitude;
        runner.Supports.Add(go);
        return sphere;
    }

    private SphereCollider TableSupport(Runner runner, int column, string edge,
        float x, float topY, float rearward)
    {
        var go = new GameObject("GloomhavenVR.TownCloth.TableSupport." + column + "." + edge)
            { layer = IgnoreRaycastLayer };
        Transform driver = runner.DriverRoot.transform;
        go.transform.SetParent(driver, false);
        // Adjacent columns are about 12.5 cm apart. The old 2.6 cm spheres left
        // a seven-centimetre unsupported gap between every pair, so a strong
        // fingertip could push triangles straight between them. Sink overlapping
        // seven-centimetre spheres below the top; their crown stays four
        // millimetres above the authored surface while their width is continuous.
        Vector3 stationPoint = new(x, topY - .066f, TableFront(x) + rearward);
        go.transform.localPosition = driver.InverseTransformPoint(_station.TransformPoint(stationPoint));
        var sphere = go.AddComponent<SphereCollider>();
        sphere.radius = .070f * driver.InverseTransformVector(
            _station.TransformVector(Vector3.up)).magnitude;
        runner.Supports.Add(go);
        return sphere;
    }

    private float TableFront(float x)
    {
        float radius = _service == 2 ? .81f : .86f;
        float depth = _service == 2 ? .38f : .46f;
        float center = _service == 2 ? 0f : .03f;
        return center - depth * Mathf.Sqrt(Mathf.Max(0f, 1f - x * x / (radius * radius)));
    }

    private Vector3 AuthoredPoint(int row, int column, float minX, float maxX)
    {
        float t = row / (DriverRows - 1f);
        float u = column / (DriverColumns - 1f);
        float x = Mathf.Lerp(minX, maxX, u);
        float fold = .0015f * Mathf.Sin(u * Mathf.PI * 6.4f + .3f);
        float surface = Mathf.Min(1f, t / .45f);
        float z = TableFront(x) + .145f * (1f - surface) - .004f * surface;
        z -= .012f * Mathf.Sin(u * Mathf.PI * 6f) * (Mathf.Max(0f, t - .45f) / .55f);
        if (_service == 3)
        {
            float fall = Mathf.Clamp01((t - .35f) / .45f);
            z -= .100f * fall * fall * (3f - 2f * fall);
        }
        float y = .9575f + fold - .49f * Mathf.Max(0f, (t - .45f) / .55f);
        y -= .018f * Mathf.Max(0f, (t - .84f) / .16f) * (1f - Mathf.Abs(u * 2f - 1f));
        return new Vector3(x, y, z);
    }

    private static Vector3 MerchantAuthoredPoint(int row, int column)
    {
        float t = row / (DriverRows - 1f);
        float u = column / (DriverColumns - 1f);
        return new Vector3(
            -1.436f - .0025f * Mathf.Sin(Mathf.PI * t) * Mathf.Sin(2f * Mathf.PI * u),
            1.590f - .600f * t + .002f * Mathf.Sin(Mathf.PI * t) * Mathf.Cos(3f * Mathf.PI * u),
            .205f + .255f * u + .004f * Mathf.Sin(2f * Mathf.PI * t) * Mathf.Sin(Mathf.PI * u));
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
        if (_service == 1) return Mathf.Pow(Mathf.Clamp01((top - p.y) / .56f), 1.25f);
        float edge = TableFront(p.x);
        float topWeave = Mathf.Clamp01((edge + .145f - p.z) / .145f);
        float hanging = Mathf.Clamp01((top - p.y) / .38f);
        // The woven surface above the table may flex a few millimetres, but must
        // not use the hanging edge's full envelope: gravity otherwise spends that
        // allowance below the solid top before a hand has touched it.
        return Mathf.Pow(Mathf.Max(.10f * topWeave, hanging), 1.25f);
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
        palmSphere.radius = WristRadiusRealMeters;
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
        probe.PreviousPalm = probe.Palm.position;
        probe.PreviousTip = probe.Tip.position;
        probe.Palm.position = new Vector3(0f, -100000f, 0f);
        probe.Tip.position = probe.Palm.position;
    }

    private static void Place(HandProbe probe, Vector3 palm, Vector3 tip,
        Vector3 wrist, float scale)
    {
        probe.PreviousPalm = probe.Palm.position;
        probe.PreviousTip = probe.Tip.position;
        probe.Palm.position = wrist;
        probe.Tip.position = tip;
        // Preserve the original 35 mm radius AT the palm rather than making
        // the newly included cuff a uniformly oversized collision ball.
        // The two endpoints use the same taper as PhysX ClothSphereColliderPair.
        float backLength = Vector3.Distance(wrist, palm);
        float fingerLength = Mathf.Max(.015f * scale, Vector3.Distance(palm, tip));
        float wristRadius = PalmRadiusRealMeters
            + (PalmRadiusRealMeters - TipRadiusRealMeters) * backLength / fingerLength;
        probe.PalmSphere.radius = Mathf.Clamp(wristRadius,
            PalmRadiusRealMeters, WristRadiusRealMeters * 1.2f) * scale;
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
        float sharedScale = VRRigDriver.RigRoot != null
            ? Mathf.Max(.0001f, Mathf.Abs(VRRigDriver.RigRoot.lossyScale.x))
            : Mathf.Max(.0001f, VRRigDriver.BaseWorldScale);
        if (VRHands.Left?.HasPose == true && at < _hands.Length)
            Place(_hands[at++], VRHands.Left.Rig.PalmCenter.position, VRHands.Left.Rig.IndexTip.position,
                VRHands.Left.Rig.Wrist.position,
                Mathf.Max(.0001f, VRHands.Left.WorldScale));
        if (VRHands.Right?.HasPose == true && at < _hands.Length)
            Place(_hands[at++], VRHands.Right.Rig.PalmCenter.position, VRHands.Right.Rig.IndexTip.position,
                VRHands.Right.Rig.Wrist.position,
                Mathf.Max(.0001f, VRHands.Right.WorldScale));

        _peers.Clear();
        NetAvatarDriver.CollectTownFacePeers(_peers);
        foreach (int peer in _peers)
        {
            if (at >= _hands.Length) break;
            if (!NetAvatarDriver.TryGetTownClothHandProbes(peer, out Vector3 left, out Vector3 leftWrist,
                    out Vector3 leftTip, out Vector3 right, out Vector3 rightWrist, out Vector3 rightTip,
                    out bool leftValid, out bool rightValid, out float peerScale)) continue;
            if (leftValid && at < _hands.Length)
                Place(_hands[at++], left, leftTip, leftWrist, peerScale);
            if (rightValid && at < _hands.Length)
                Place(_hands[at++], right, rightTip, rightWrist, peerScale);
        }
        while (at < _hands.Length) Park(_hands[at++]);

        int headAt = 0;
        if (VRRigDriver.HeadCamera != null)
            PlaceHead(_heads[headAt++], VRRigDriver.HeadCamera.transform.position, sharedScale);
        foreach (int peer in _peers)
        {
            if (headAt >= _heads.Length) break;
            if (NetAvatarDriver.TryGetTownClothHead(peer, out Vector3 head, out float headScale))
                PlaceHead(_heads[headAt++], head, headScale);
        }
        while (headAt < _heads.Length) Park(_heads[headAt++]);
    }

    private bool AnyProbeWithin(Runner runner, float realMargin, out float closestReal)
    {
        closestReal = float.MaxValue;
        Renderer? renderer = runner.VisibleRenderer;
        if (renderer == null) return false;
        Bounds bounds = renderer.bounds;
        float stationScale = Mathf.Max(.0001f, _station.TransformVector(Vector3.right).magnitude);
        float margin = stationScale * realMargin;
        bounds.Expand((margin + stationScale * .14f) * 2f);
        foreach (HandProbe probe in _hands)
            if (ProbeWithin(runner, probe, bounds, margin, stationScale, ref closestReal)) return true;
        foreach (HandProbe probe in _heads)
            if (ProbeWithin(runner, probe, bounds, margin, stationScale, ref closestReal)) return true;
        return false;
    }

    private static bool ProbeWithin(Runner runner, HandProbe probe, in Bounds broadphase,
        float margin, float stationScale, ref float closestReal)
    {
        float radius = Mathf.Max(probe.PalmSphere.radius, probe.TipSphere.radius);
        Bounds expanded = broadphase;
        expanded.Expand(radius * 2f);
        // Both endpoints may be outside a narrow side drape while the back of
        // the hand passes through it. Endpoint-only broadphase dropped exactly
        // those contacts in the headset clip. Test the capsule's whole swept
        // AABB before the narrow-phase surface query.
        Vector3 span = probe.Tip.position - probe.Palm.position;
        var probeBounds = new Bounds((probe.Palm.position + probe.Tip.position) * .5f,
            new Vector3(Mathf.Abs(span.x), Mathf.Abs(span.y), Mathf.Abs(span.z)));
        probeBounds.Expand(radius * 2f);
        if (!expanded.Intersects(probeBounds)) return false;

        // Build 566 used only a point-in-AABB test. PhysX could already push a runner with the
        // probe's sphere/capsule while that separate test still said "no contact"; the render path
        // then captured the displaced sheet as its new zero every frame, making real physics wholly
        // invisible. Measure the tapered capsule against the current physical
        // sheet. A broad altar runner has over 10 cm between adjacent columns,
        // so a fingertip can hit a triangle while every particle is far away.
        // Check vertices first; only a miss visits nearby triangles.
        Vector3 a = probe.Palm.position, b = probe.Tip.position;
        Transform driver = runner.DriverRoot.transform;
        float driverScale = UniformScale(driver);
        Vector3 localA = driver.InverseTransformPoint(a), localB = driver.InverseTransformPoint(b);
        float localLimit = (radius + margin) / driverScale;
        float bestGap = float.MaxValue;
        Vector3[] surface = runner.ContactSurface.Length == runner.DriverRest.Length
            ? runner.ContactSurface : runner.DriverRest;
        for (int i = 0; i < surface.Length; i++)
        {
            float distance = DistanceSquaredToSegment(surface[i], localA, localB, out float t);
            float gap = Mathf.Sqrt(distance) * driverScale
                - Mathf.Lerp(probe.PalmSphere.radius, probe.TipSphere.radius, t);
            bestGap = Mathf.Min(bestGap, gap);
            if (gap <= margin)
            {
                closestReal = Mathf.Min(closestReal,
                    Mathf.Max(0f, gap) / stationScale);
                return true;
            }
        }
        Vector3 lower = Vector3.Min(localA, localB) - Vector3.one * localLimit;
        Vector3 upper = Vector3.Max(localA, localB) + Vector3.one * localLimit;
        for (int row = 0; row < DriverRows - 1; row++)
        for (int column = 0; column < DriverColumns - 1; column++)
        {
            int vertex = row * DriverColumns + column;
            Vector3 topLeft = surface[vertex], topRight = surface[vertex + 1];
            Vector3 bottomLeft = surface[vertex + DriverColumns];
            Vector3 bottomRight = surface[vertex + DriverColumns + 1];
            if (!OverlapsProbeBox(topLeft, topRight, bottomLeft, bottomRight, lower, upper))
                continue;
            if (TriangleWithin(localA, localB, topLeft, bottomLeft, topRight,
                    probe.PalmSphere.radius, probe.TipSphere.radius,
                    driverScale, margin, stationScale, ref closestReal, ref bestGap)
                || TriangleWithin(localA, localB, topRight, bottomLeft, bottomRight,
                    probe.PalmSphere.radius, probe.TipSphere.radius,
                    driverScale, margin, stationScale, ref closestReal, ref bestGap)) return true;
        }
        closestReal = Mathf.Min(closestReal,
            Mathf.Max(0f, bestGap) / stationScale);
        return false;
    }

    private static bool TriangleWithin(Vector3 a, Vector3 b, Vector3 p, Vector3 q, Vector3 r,
        float backRadius, float tipRadius, float driverScale, float margin, float stationScale,
        ref float closestReal, ref float bestGap)
    {
        float distance = DistanceSquaredSegmentTriangle(a, b, p, q, r, out float t);
        float gap = Mathf.Sqrt(distance) * driverScale - Mathf.Lerp(backRadius, tipRadius, t);
        bestGap = Mathf.Min(bestGap, gap);
        if (gap > margin) return false;
        closestReal = Mathf.Min(closestReal, Mathf.Max(0f, gap) / stationScale);
        return true;
    }

    private static float DistanceSquaredToSegment(Vector3 point, Vector3 a, Vector3 b, out float t)
    {
        Vector3 edge = b - a;
        float length = edge.sqrMagnitude;
        if (length < .000001f) { t = 0f; return (point - a).sqrMagnitude; }
        t = Mathf.Clamp01(Vector3.Dot(point - a, edge) / length);
        return (point - (a + edge * t)).sqrMagnitude;
    }

    private static bool OverlapsProbeBox(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
        Vector3 lower, Vector3 upper)
    {
        return Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x)) >= lower.x
            && Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)) <= upper.x
            && Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y)) >= lower.y
            && Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)) <= upper.y
            && Mathf.Max(Mathf.Max(a.z, b.z), Mathf.Max(c.z, d.z)) >= lower.z
            && Mathf.Min(Mathf.Min(a.z, b.z), Mathf.Min(c.z, d.z)) <= upper.z;
    }

    private static float DistanceSquaredSegmentTriangle(Vector3 a, Vector3 b,
        Vector3 p, Vector3 q, Vector3 r, out float segmentT)
    {
        Vector3 normal = Vector3.Cross(q - p, r - p);
        Vector3 direction = b - a;
        float divisor = Vector3.Dot(normal, direction);
        if (normal.sqrMagnitude > 1e-10f && Mathf.Abs(divisor) > 1e-8f)
        {
            float t = Vector3.Dot(normal, p - a) / divisor;
            if (t >= 0f && t <= 1f)
            {
                Vector3 hit = a + direction * t;
                if (Vector3.Dot(normal, Vector3.Cross(q - p, hit - p)) >= -1e-7f
                    && Vector3.Dot(normal, Vector3.Cross(r - q, hit - q)) >= -1e-7f
                    && Vector3.Dot(normal, Vector3.Cross(p - r, hit - r)) >= -1e-7f)
                { segmentT = t; return 0f; }
            }
        }

        // Endpoint faces and the three edge pairs cover the closest features
        // of two convex primitives even when the segment misses the plane.
        float best = (a - ClosestPointOnTriangle(a, p, q, r)).sqrMagnitude;
        segmentT = 0f;
        float candidate = (b - ClosestPointOnTriangle(b, p, q, r)).sqrMagnitude;
        if (candidate < best) { best = candidate; segmentT = 1f; }
        candidate = DistanceSquaredSegments(a, b, p, q, out float edgeT);
        if (candidate < best) { best = candidate; segmentT = edgeT; }
        candidate = DistanceSquaredSegments(a, b, q, r, out edgeT);
        if (candidate < best) { best = candidate; segmentT = edgeT; }
        candidate = DistanceSquaredSegments(a, b, r, p, out edgeT);
        if (candidate < best) { best = candidate; segmentT = edgeT; }
        return best;
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        // Barycentric Voronoi regions, used only after the cheap particle gate
        // missed a collision with the interior of a rendered cloth triangle.
        Vector3 ab = b - a, ac = c - a, ap = point - a;
        if (Vector3.Cross(ab, ac).sqrMagnitude <= 1e-12f)
        {
            // Native cloth can collapse a triangle temporarily at a pin or
            // table lip. Treat that geometry as three edges, never divide by
            // a zero barycentric denominator during the contact gate.
            Vector3 best = a + ab * ClosestFraction(point, a, b);
            float distance = (point - best).sqrMagnitude;
            Vector3 candidate = b + (c - b) * ClosestFraction(point, b, c);
            if ((point - candidate).sqrMagnitude < distance)
            { best = candidate; distance = (point - candidate).sqrMagnitude; }
            candidate = c + (a - c) * ClosestFraction(point, c, a);
            if ((point - candidate).sqrMagnitude < distance) best = candidate;
            return best;
        }
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;
        Vector3 bp = point - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = point - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float denominator = 1f / (va + vb + vc);
        return a + ab * (vb * denominator) + ac * (vc * denominator);
    }

    private static float ClosestFraction(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 edge = b - a;
        float length = edge.sqrMagnitude;
        return length > 1e-12f ? Mathf.Clamp01(Vector3.Dot(point - a, edge) / length) : 0f;
    }

    private static float DistanceSquaredSegments(Vector3 p1, Vector3 q1,
        Vector3 p2, Vector3 q2, out float alongFirst)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, separation = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, separation);
        float s, t;
        if (a <= 1e-10f && e <= 1e-10f) { alongFirst = 0f; return separation.sqrMagnitude; }
        if (a <= 1e-10f) { s = 0f; t = Mathf.Clamp01(f / e); }
        else
        {
            float c = Vector3.Dot(d1, separation);
            if (e <= 1e-10f) { t = 0f; s = Mathf.Clamp01(-c / a); }
            else
            {
                float b = Vector3.Dot(d1, d2), denominator = a * e - b * b;
                s = denominator > 1e-10f ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
            }
        }
        alongFirst = s;
        return (separation + d1 * s - d2 * t).sqrMagnitude;
    }

    private static void SetFreedom(Runner runner, float scale)
    {
        ClothSkinningCoefficient[] coefficients = runner.Cloth.coefficients;
        for (int n = 0; n < coefficients.Length; n++)
            coefficients[n].maxDistance = Mathf.Max(
                runner.DriverFreedom[n] * IdleFreedomRealMeters * runner.StationUnitInDriver,
                runner.DriverMaximum[n] * scale);
        runner.Cloth.coefficients = coefficients;
        runner.Cloth.ClearTransformMotion();
    }

    private void RestoreAfterContact(Runner runner, float dt)
    {
        bool near = AnyProbeWithin(runner, .16f, out float nearDistance);
        float contactDistance = float.MaxValue;
        // The exact same physical surface and hand capsules are queried at
        // both margins. A miss at 16 cm makes an 18 mm hit impossible, so do
        // not traverse the whole sheet a second time while idle.
        bool rawContact = near && AnyProbeWithin(runner, .018f, out contactDistance);
        runner.ContactHold = rawContact ? ContactHoldSeconds
            : Mathf.Max(0f, runner.ContactHold - Mathf.Max(0f, dt));
        bool contact = rawContact || (near && runner.Contacting && runner.ContactHold > 0f);
        if (contact && !rawContact && VRLog.WantsDebug)
            runner.DebugSuppressedContactGaps++;
        if (near && !runner.Interactive)
        {
            // Capture the settled cloth BEFORE enabling the large travel envelope.
            // The previous deferred capture ran in Render, after the physics step:
            // a hand arriving during that step became the new zero and its first
            // displacement was invisible. Re-entry still starts at the authored
            // renderer without rebuilding the solver. Pull once on entry rather
            // than continuously reading the idle solver every display frame.
            runner.ContactSurface = runner.Cloth.vertices;
            if (runner.ContactSurface.Length == runner.EpisodeOrigin.Length)
                Array.Copy(runner.ContactSurface, runner.EpisodeOrigin, runner.EpisodeOrigin.Length);
            SetFreedom(runner, 1f);
            runner.Interactive = true;
        }
        if (contact && !runner.Contacting)
        {
            runner.Cloth.externalAcceleration = Physics.gravity * runner.StationUnitInDriver;
        }
        runner.Contacting = contact;
        // The physical solver is the authority on whether a collider touched the
        // sheet. The separate 18 mm contact query is diagnostic and arms gravity,
        // but it must not be a visibility gate: the Build 579 headset log never
        // marked the red side drape as contact while the glove visibly crossed it.
        // Show the native cloth whenever a hand enters the preparation margin,
        // and let it settle physically while that hand remains nearby. This also
        // prevents the broad altar runner from fading out between alternating
        // contact edges in one continuous gesture.
        float previousWeight = runner.DeformationWeight;
        runner.DeformationWeight = Mathf.MoveTowards(runner.DeformationWeight, near ? 1f : 0f,
            Mathf.Max(0f, dt) / (near ? ContactPresentationSeconds : .48f));
        if (!near && previousWeight > 0f && runner.DeformationWeight <= 0f
            && runner.ContactSurface.Length == runner.EpisodeOrigin.Length)
        {
            // A second touch can begin without leaving the preparation margin.
            // Capture the settled solver only once, after the visible release is
            // complete, so the next short touch is visible from its first frame.
            Array.Copy(runner.ContactSurface, runner.EpisodeOrigin, runner.EpisodeOrigin.Length);
        }
        if (!near && runner.Interactive && runner.DeformationWeight <= 0f)
        {
            // The visible delta has returned exactly to the authored drape. Pin the
            // bounded hidden solver until another probe approaches. Its stale offset
            // never becomes visible because the next episode captures a fresh origin.
            SetFreedom(runner, 0f);
            runner.Cloth.externalAcceleration = Vector3.zero;
            runner.Interactive = false;
        }
        else if (!contact && runner.DeformationWeight <= 0f)
        {
            // Stay prepared while a hand remains nearby, but stop accumulating a
            // gravity-only fold that could become the next episode's starting kick.
            runner.Cloth.externalAcceleration = Vector3.zero;
        }
        if (VRLog.WantsDebug && (near != runner.DebugNear || contact != runner.DebugContact))
        {
            bool endedContact = runner.DebugContact && !contact;
            runner.DebugNear = near; runner.DebugContact = contact;
            float nearest = contact ? contactDistance : nearDistance;
            VRLog.Debug("TownServices", "Town cloth proximity edge: service=" + _service
                + " runner='" + runner.Filter.name + "' near=" + near + " contact=" + contact
                + " nearest=" + (nearest < float.MaxValue ? nearest.ToString("F3") + "m" : "parked")
                + " driverEnabled=" + runner.Cloth.enabled + " interactive=" + runner.Interactive
                + " visibleWeight=" + runner.DeformationWeight.ToString("F2")
                + (endedContact ? " shownPeak=" + runner.DebugShownPeak.ToString("F3") + "m"
                    + " heldSolverGaps=" + runner.DebugSuppressedContactGaps : ""));
            if (endedContact) { runner.DebugShownPeak = 0f; runner.DebugSuppressedContactGaps = 0; }
        }
    }

    private void PrepareProjection(Runner runner)
    {
        // PhysX Cloth collides at particles, while this furniture's authored
        // 13-column surface spans 12.5 cm between adjacent particles. A real
        // fingertip can cross a triangle interior without any particle touching
        // its collider. The visible layer needs a continuous capsule-to-surface
        // constraint as well as the native garment-style sphere pairs. Latch
        // which side the hand approached from before it crosses the thin sheet;
        // otherwise the correction flips sides halfway through a push.
        runner.ProjectionCount = 0;
        float stationScale = Mathf.Max(.0001f, _station.TransformVector(Vector3.up).magnitude);
        float stationReachSquared = stationScale * stationScale * 3f * 3f;
        bool handAtStation = false;
        for (int i = 0; i < _hands.Length; i++)
            if ((_hands[i].Palm.position - _station.position).sqrMagnitude <= stationReachSquared)
            { handAtStation = true; break; }
        if (!handAtStation)
        {
            Array.Clear(runner.ProbeSide, 0, runner.ProbeSide.Length);
            return;
        }
        Renderer? renderer = runner.VisibleRenderer;
        if (renderer == null) return;
        Bounds nearby = renderer.bounds;
        nearby.Expand(stationScale * .72f);
        Matrix4x4 toStation = _station.worldToLocalMatrix;
        Matrix4x4 driverToStation = toStation * runner.DriverRoot.transform.localToWorldMatrix;
        for (int i = 0; i < _hands.Length; i++)
        {
            HandProbe hand = _hands[i];
            Vector3 wrist = hand.Palm.position, tip = hand.Tip.position;
            if (wrist.y < -1000f) { runner.ProbeSide[i] = 0; continue; }
            Vector3 span = tip - wrist;
            var capsuleBounds = new Bounds((wrist + tip) * .5f,
                new Vector3(Mathf.Abs(span.x), Mathf.Abs(span.y), Mathf.Abs(span.z)));
            capsuleBounds.Expand(2f * Mathf.Max(hand.PalmSphere.radius, hand.TipSphere.radius));
            if (!nearby.Intersects(capsuleBounds)) { runner.ProbeSide[i] = 0; continue; }
            if (runner.ProbeSide[i] == 0)
            {
                Vector3 sample = (wrist + tip) * .5f;
                Vector3 previous = (hand.PreviousPalm + hand.PreviousTip) * .5f;
                if (previous.y > -1000f && (previous - sample).sqrMagnitude <
                    stationScale * stationScale * .4f * .4f) sample = previous;
                sample = toStation.MultiplyPoint3x4(sample);
                float closest = float.MaxValue;
                Vector3 nearest = Vector3.zero;
                for (int n = 0; n < runner.DriverRest.Length; n++)
                {
                    Vector3 p = driverToStation.MultiplyPoint3x4(runner.DriverRest[n]);
                    float distance = (p - sample).sqrMagnitude;
                    if (distance >= closest) continue;
                    closest = distance;
                    nearest = p;
                }
                runner.ProbeSide[i] = sample.z <= nearest.z ? 1 : -1;
            }
            runner.Projection[runner.ProjectionCount++] = new ProjectionProbe
            {
                Wrist = toStation.MultiplyPoint3x4(wrist),
                Tip = toStation.MultiplyPoint3x4(tip),
                WristRadius = hand.PalmSphere.radius / stationScale,
                TipRadius = hand.TipSphere.radius / stationScale,
                Side = runner.ProbeSide[i]
            };
        }
    }

    private static float ProjectClearance(Runner runner, Vector3 point, float freedom)
    {
        float wanted = 0f;
        if (freedom > .12f)
        {
            float spreadSquared = ClearanceSpreadRealMeters * ClearanceSpreadRealMeters;
            for (int i = 0; i < runner.ProjectionCount; i++)
            {
                ProjectionProbe hand = runner.Projection[i];
                DistanceSquaredToSegment(point, hand.Wrist, hand.Tip, out float t);
                Vector3 centre = Vector3.Lerp(hand.Wrist, hand.Tip, t);
                Vector3 offset = point - centre;
                float depth = offset.z * hand.Side;
                float lateralSquared = Mathf.Max(0f, offset.sqrMagnitude - depth * depth);
                if (lateralSquared > .34f * .34f) continue;
                float radius = Mathf.Lerp(hand.WristRadius, hand.TipRadius, t);
                float broadSurface = (radius + HandClearanceRealMeters)
                    * Mathf.Exp(-.5f * lateralSquared / spreadSquared);
                float push = Mathf.Clamp((broadSurface - depth) * Mathf.Clamp01(freedom * 2f),
                    0f, ClearanceLimitRealMeters);
                if (push > Mathf.Abs(wanted)) wanted = push * hand.Side;
            }
        }
        return wanted;
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
        if (_disposed || _runners.Length == 0) return;
        UpdateHands();
        foreach (Runner runner in _runners)
        {
            if (!visible) continue;
            FollowSource(runner);
            if (VRLog.WantsDebug && !runner.DebugReady)
            {
                runner.DebugReady = true;
                VRLog.Debug("TownServices", "Town cloth production probes ready: service=" + _service
                    + " runner='" + runner.Filter.name + "' localLeft=" + (VRHands.Left?.HasPose == true)
                    + " localRight=" + (VRHands.Right?.HasPose == true)
                    + " rigScale=" + (VRRigDriver.RigRoot != null
                        ? VRRigDriver.RigRoot.lossyScale.x.ToString("F2") : "none")
                    + " driverEnabled=" + runner.Cloth.enabled
                    + " physicalParticles=" + runner.ContactSurface.Length + ".");
            }
            RestoreAfterContact(runner, dt);
            PrepareProjection(runner);
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
        if (_disposed || _runners.Length == 0) return;
        UpdateHands();
        for (int i = 0; i < _runners.Length; i++)
        {
            Runner runner = _runners[i];
            FollowSource(runner);
            RestoreAfterContact(runner, Time.unscaledDeltaTime);
            PrepareProjection(runner);
            TownClothRunnerState state = i == 0 ? first : second;
            float prediction = Mathf.Clamp(elapsed, 0f, .12f);
            state.Left += state.LeftVelocity * prediction;
            state.Right += state.RightVelocity * prediction;
            if (visible) Render(runner, state, true);
            runner.State = state;
        }
    }

    private TownClothRunnerState Render(Runner runner, in TownClothRunnerState owner, bool correctToOwner)
    {
        if (Time.unscaledTime < runner.NextRender) return runner.State;
        runner.NextRender = Time.unscaledTime + 1f / 90f;
        bool ownerMoving = (runner.State.Left.sqrMagnitude + runner.State.Right.sqrMagnitude
            + (correctToOwner ? owner.Left.sqrMagnitude + owner.Right.sqrMagnitude : 0f)) > 1e-8f;
        if (!runner.Interactive && runner.DeformationWeight <= 0f
            && runner.ProjectionCount == 0 && !runner.ActiveClearance && !ownerMoving)
            return default;
        // Cloth.vertices allocates. Capture it exactly once and use the same
        // solver snapshot for rendering, owner measurement and peer correction.
        Vector3[] simulated = runner.Cloth.vertices;
        if (simulated.Length != runner.DriverRest.Length) return runner.State;
        // Keep the next frame's contact gate on the same physical sheet that is
        // shown now. This costs no extra Cloth.vertices pull or allocation: the
        // render/replication snapshot is already the sole PhysX read per frame.
        runner.ContactSurface = simulated;

        if (_service == 3) SmoothNarrowDrape(runner, simulated);
        else
            for (int n = 0; n < simulated.Length; n++)
                runner.VisualDelta[n] = simulated[n] - runner.EpisodeOrigin[n];
        Matrix4x4 toStation = _station.worldToLocalMatrix * runner.Filter.transform.localToWorldMatrix;
        Matrix4x4 fromStation = runner.Filter.transform.worldToLocalMatrix * _station.localToWorldMatrix;
        Vector3 localX = runner.Filter.transform.InverseTransformVector(_station.TransformVector(Vector3.right));
        Vector3 localZ = runner.Filter.transform.InverseTransformVector(
            _station.TransformVector(_service == 1 ? Vector3.up : Vector3.forward));
        Vector2 left = Vector2.zero, right = Vector2.zero;
        float leftWeight = 0f, rightWeight = 0f;
        bool measureDebug = runner.Contacting && VRLog.WantsDebug;
        float debugScale = measureDebug
            ? Mathf.Max(.0001f, _station.TransformVector(Vector3.right).magnitude) : 1f;
        bool updateClearance = runner.ProjectionCount != 0 || runner.ActiveClearance;
        runner.ActiveClearance = false;
        for (int n = 0; n < runner.Rest.Length; n++)
        {
            Vector3 driverDelta = BlendedDelta(in runner.VisibleDriverMap[n], runner.VisualDelta);
            Vector3 worldDelta = runner.DriverRoot.transform.TransformVector(driverDelta)
                * runner.DeformationWeight;
            Vector3 shown = runner.Rest[n] + runner.Filter.transform.InverseTransformVector(worldDelta);
            if (updateClearance)
            {
                float target = runner.ProjectionCount == 0 ? 0f : ProjectClearance(runner,
                    toStation.MultiplyPoint3x4(shown), runner.Freedom[n]);
                float clearance = runner.Clearance[n];
                if (Mathf.Abs(target) > Mathf.Abs(clearance) || target * clearance < 0f)
                    clearance = target;
                else
                    clearance = Mathf.MoveTowards(clearance, target,
                        ClearanceLimitRealMeters * Mathf.Max(0f, Time.unscaledDeltaTime) / .35f);
                runner.Clearance[n] = clearance;
                runner.ActiveClearance |= Mathf.Abs(clearance) > .00001f;
                if (clearance != 0f)
                    shown += fromStation.MultiplyVector(Vector3.forward * clearance);
            }
            runner.Shown[n] = shown;
            Vector3 offset = toStation.MultiplyVector(shown - runner.Rest[n]);
            float weight = runner.Freedom[n] * runner.Freedom[n];
            float side = runner.Side[n];
            float lw = weight * (1f - side), rw = weight * side;
            Vector2 direction = new(offset.x, _service == 1 ? offset.y : offset.z);
            left += direction * lw; right += direction * rw;
            leftWeight += lw; rightWeight += rw;
            if (measureDebug)
                runner.DebugShownPeak = Mathf.Max(runner.DebugShownPeak,
                    Vector3.Distance(runner.Filter.transform.TransformPoint(runner.Rest[n]),
                        runner.Filter.transform.TransformPoint(shown)) / debugScale);
        }
        TownClothRunnerState measured = new()
        {
            Left = Vector2.ClampMagnitude(left / Mathf.Max(.001f, leftWeight), .09f),
            Right = Vector2.ClampMagnitude(right / Mathf.Max(.001f, rightWeight), .09f)
        };
        if (correctToOwner)
            for (int n = 0; n < runner.Shown.Length; n++)
            {
                float side = runner.Side[n];
                Vector2 wanted = Vector2.Lerp(owner.Left, owner.Right, side);
                Vector2 actual = Vector2.Lerp(measured.Left, measured.Right, side);
                Vector2 correction = Vector2.ClampMagnitude(wanted - actual, .08f);
                runner.Shown[n] += runner.Freedom[n]
                    * (localX * correction.x + localZ * correction.y);
                // Owner-state matching is intentionally small, but it runs
                // after the local capsule constraint. Recheck the visible
                // point so replication cannot pull a peer's cloth through
                // the hand used for that peer's physical presentation.
                if (runner.ProjectionCount != 0)
                {
                    float peerClearance = ProjectClearance(runner,
                        toStation.MultiplyPoint3x4(runner.Shown[n]), runner.Freedom[n]);
                    if (peerClearance != 0f)
                        runner.Shown[n] += fromStation.MultiplyVector(Vector3.forward * peerClearance);
                }
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
            if (!visible)
            {
                runner.Contacting = false;
                runner.ContactHold = 0f;
                runner.ProjectionCount = 0;
                runner.ActiveClearance = false;
                Array.Clear(runner.ProbeSide, 0, runner.ProbeSide.Length);
                Array.Clear(runner.Clearance, 0, runner.Clearance.Length);
            }
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
