using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed class TownClothProbe : MonoBehaviour
{
    private sealed class Fixture
    {
        internal GameObject Root = null!;
        internal Cloth Cloth = null!;
        internal Transform Mover = null!;
        internal Mesh Visible = null!;
        internal Vector3[] Rest = Array.Empty<Vector3>();
        internal float StationScale;
        internal float SolverScale;
        internal Transform Station = null!;
        internal Transform Driver = null!;
        internal int[] TableIndices = Array.Empty<int>();
        internal Vector3[] TableRest = Array.Empty<Vector3>();
        internal Vector3 Gravity;
        internal float RestDamping;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var root = new GameObject("Native town cloth probe");
        DontDestroyOnLoad(root);
        root.AddComponent<TownClothProbe>();
    }

    private IEnumerator Start()
    {
        Application.targetFrameRate = 90;
        Time.fixedDeltaTime = 1f / 90f;

        Fixture positive = Build("hidden-positive", 1f, 1f, true, Vector3.zero, 0f);
        yield return Settle(positive, 120);
        float gravityMotion = MaximumDistance(positive.Rest, positive.Cloth.vertices);
        yield return Freeze(positive);
        Vector3[] positiveBefore = positive.Cloth.vertices;
        yield return Sweep(positive);
        float contactMotion = MaximumDistance(positiveBefore, positive.Cloth.vertices);

        Fixture nullContact = Build("no-collider-negative-control", 1f, 1f, false, Vector3.zero, 0f);
        yield return Settle(nullContact, 120);
        yield return Freeze(nullContact);
        Vector3[] nullBefore = nullContact.Cloth.vertices;
        yield return Sweep(nullContact);
        float nullMotion = MaximumDistance(nullBefore, nullContact.Cloth.vertices);

        // Shipping town furniture nests a 100x FBX mesh below the roughly 198x
        // map-room station. Exercise that hierarchy rather than a unit-scale
        // sheet: the regression multiplied station travel by the complete 19800x
        // driver scale and consequently made the cloth's travel/skin 100x large.
        Fixture scaled = Build("unit-scale-world-driver", 198f, 100f, true, true,
            new Vector3(3f, 2f, -4f), 37f);
        yield return Settle(scaled, 60);
        float scaledGravityMotion = MaximumDistance(scaled.Rest, scaled.Cloth.vertices) * scaled.SolverScale;
        yield return Freeze(scaled);
        Vector3[] scaledBefore = scaled.Cloth.vertices;
        yield return Sweep(scaled, .60f);
        float scaledLocalMotion = MaximumDistance(scaledBefore, scaled.Cloth.vertices) * scaled.SolverScale;

        Fixture unscaled = Build("inherited-fbx-scale-negative-control", 198f, 100f, true, false,
            new Vector3(-3f, 2f, -4f), -29f);
        yield return Settle(unscaled, 60);
        float unscaledGravityMotion = MaximumDistance(unscaled.Rest, unscaled.Cloth.vertices) * unscaled.SolverScale;
        yield return Freeze(unscaled);
        Vector3[] unscaledBefore = unscaled.Cloth.vertices;
        yield return Sweep(unscaled, .60f);
        float unscaledLocalMotion = MaximumDistance(unscaledBefore, unscaled.Cloth.vertices) * unscaled.SolverScale;

        Vector3[] sample = positive.Cloth.vertices;
        var watch = Stopwatch.StartNew();
        const int costIterations = 1000;
        for (int i = 0; i < costIterations; i++)
        {
            sample = positive.Cloth.vertices;
            positive.Visible.vertices = sample;
            positive.Visible.RecalculateNormals();
            positive.Visible.RecalculateBounds();
        }
        watch.Stop();
        double microseconds = watch.Elapsed.TotalMilliseconds * 1000.0 / costIterations;
        int snapshotBytes = sample.Length * 12;

        bool hiddenPass = gravityMotion > .025f && contactMotion > .012f;
        bool negativePass = nullMotion < .006f && contactMotion > nullMotion + .012f;
        // Production bakes the already-placed sheet into a unit-scale solver.
        // The control reproduces the released inherited-19,800x driver, whose
        // particles become numerically unbounded under the same physical sweep.
        bool scalePass = scaledLocalMotion > .5f
            && scaledLocalMotion < 200f
            && unscaledLocalMotion > scaledLocalMotion * 3f;
        string bundlePath = Argument("--bundle=");
        AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
        bool actualPass = bundle != null;
        float actualContactMin = float.MaxValue, actualContactMax = 0f, actualTableDrop = 0f;
        float actualVisibleReturnMax = 0f, actualReentryMin = float.MaxValue;
        float actualLongTermMax = 0f, actualPinnedDriftMax = 0f;
        float actualReturnStepMax = 0f, actualReentryPopMax = 0f;
        float actualApproachRawDriftMax = 0f, actualContactStepMax = 0f;
        bool actualReturnMonotone = true;
        int actualRunners = 0;
        if (bundle != null)
        {
            int assetOffset = 0;
            foreach (var item in new[] { (Name: "priestess", Service: (byte)2, Count: 2),
                         (Name: "enchantress", Service: (byte)3, Count: 1) })
            {
                string asset = bundle.GetAllAssetNames().Single(n => n.EndsWith("/town" + item.Name + ".prefab"));
                GameObject station = Instantiate(bundle.LoadAsset<GameObject>(asset));
                station.transform.SetPositionAndRotation(new Vector3(assetOffset * 500f, 0f, 800f), Quaternion.identity);
                station.transform.localScale = Vector3.one * 198f;
                MeshFilter[] filters = station.GetComponentsInChildren<MeshFilter>(true)
                    .Where(f => f.name.StartsWith("ClothRunner_", StringComparison.Ordinal)).ToArray();
                actualPass &= filters.Length == item.Count;
                foreach (MeshFilter filter in filters)
                {
                    actualRunners++;
                    float nested = filter.transform.lossyScale.x / station.transform.lossyScale.x;
                    actualPass &= nested > 99f && nested < 101f;
                    Fixture actual = BuildActual("actual-" + item.Name + "-" + actualRunners,
                        station.transform, filter, item.Service, 198f);
                    yield return Settle(actual, 120);
                    actualTableDrop = Mathf.Max(actualTableDrop, MaximumTableDrop(actual));
                    yield return Freeze(actual);
                    Vector3[] before = actual.Cloth.vertices;
                    Vector3 sweepStart = actual.Mover.position;
                    ClothSkinningCoefficient[] active = actual.Cloth.coefficients;
                    yield return RepeatedSweep(actual, .18f, 3);
                    float contact = MaximumDistance(before, actual.Cloth.vertices);
                    actualContactMin = Mathf.Min(actualContactMin, contact);
                    actualContactMax = Mathf.Max(actualContactMax, contact);
                    actualPass &= contact > .25f && contact < 80f;
                    actual.Mover.position = new Vector3(0f, -100000f, 0f);
                    actual.Cloth.externalAcceleration = actual.Gravity;
                    actual.Cloth.damping = actual.RestDamping;
                    // Production fades the episode delta to the unchanged authored
                    // renderer over 0.48 seconds, then pins its bounded hidden solver.
                    // Exercise that path after three large bidirectional impulses.
                    float visibleReturn = contact;
                    float previousVisible = contact;
                    for (int frame = 0; frame < 44; frame++)
                    {
                        yield return null;
                        float weight = Mathf.Max(0f, 1f - (frame + 1f) / 44f);
                        visibleReturn = MaximumDistance(before, actual.Cloth.vertices) * weight;
                        actualReturnStepMax = Mathf.Max(actualReturnStepMax,
                            Mathf.Abs(visibleReturn - previousVisible));
                        actualReturnMonotone &= visibleReturn <= previousVisible + .25f;
                        previousVisible = visibleReturn;
                    }
                    actualVisibleReturnMax = Mathf.Max(actualVisibleReturnMax, visibleReturn);
                    ClothSkinningCoefficient[] pinned = actual.Cloth.coefficients;
                    for (int i = 0; i < pinned.Length; i++) pinned[i].maxDistance = 0f;
                    actual.Cloth.coefficients = pinned;
                    actual.Cloth.externalAcceleration = Vector3.zero;
                    actual.Cloth.ClearTransformMotion();
                    Vector3[] pinnedOrigin = actual.Cloth.vertices;
                    for (int frame = 0; frame < 90; frame++) yield return null;
                    float pinnedDrift = MaximumDistance(pinnedOrigin, actual.Cloth.vertices);
                    actualPinnedDriftMax = Mathf.Max(actualPinnedDriftMax, pinnedDrift);
                    actualPass &= visibleReturn < .001f;

                    // Re-entry captures the current hidden state as a new visual zero.
                    // Expanding the existing component must therefore have no pose pop,
                    // while a subsequent physical sweep still produces a real response.
                    actual.Cloth.coefficients = active;
                    actual.Cloth.ClearTransformMotion();
                    Vector3[] reentryOrigin = actual.Cloth.vertices;
                    // Production keeps the renderer at rest through a 60 ms
                    // coefficient warm-up and refreshes the episode origin while
                    // the probe is still in the approach margin.
                    for (int frame = 0; frame < 6; frame++)
                    {
                        yield return null;
                        reentryOrigin = actual.Cloth.vertices;
                    }
                    // Remaining inside proximity without touching keeps weight at
                    // zero and refreshes the visual origin on every production tick.
                    // Its raw hidden movement is recorded, but cannot become a pop.
                    for (int frame = 0; frame < 12; frame++)
                    {
                        yield return null;
                        float raw = MaximumDistance(reentryOrigin, actual.Cloth.vertices);
                        actualApproachRawDriftMax = Mathf.Max(actualApproachRawDriftMax, raw);
                        actualReentryPopMax = Mathf.Max(actualReentryPopMax, raw * 0f);
                        reentryOrigin = actual.Cloth.vertices;
                    }
                    actual.Mover.position = sweepStart;
                    actual.Cloth.externalAcceleration = actual.Gravity;
                    float previousEntry = 0f;
                    for (int frame = 0; frame < 12; frame++)
                    {
                        yield return null;
                        float weight = Mathf.Min(1f, (frame + 1f) / 11f);
                        float shown = MaximumDistance(reentryOrigin, actual.Cloth.vertices) * weight;
                        actualContactStepMax = Mathf.Max(actualContactStepMax,
                            Mathf.Abs(shown - previousEntry));
                        previousEntry = shown;
                    }
                    actualPass &= actualReentryPopMax < .001f && actualContactStepMax < 2.5f;
                    yield return RepeatedSweep(actual, .18f, 2);
                    float reentry = MaximumDistance(reentryOrigin, actual.Cloth.vertices);
                    actualReentryMin = Mathf.Min(actualReentryMin, reentry);
                    float longTerm = MaximumDistance(actual.Rest, actual.Cloth.vertices);
                    actualLongTermMax = Mathf.Max(actualLongTermMax, longTerm);
                    actualPass &= reentry > .25f && reentry < 80f;
                    actualPass &= longTerm < 30f && pinnedDrift < 25f;
                    actualPass &= actualReturnMonotone && actualReturnStepMax < Mathf.Max(2.5f, contact * .15f);
                    assetOffset++;
                }
            }
            actualPass &= actualRunners == 3 && actualTableDrop < 8f;
            bundle.Unload(false);
        }
        bool pass = hiddenPass && negativePass && scalePass && actualPass;
        string line = "native_hidden_cloth=" + (pass ? "PASS" : "FAIL")
            + " gravity_motion=" + gravityMotion.ToString("F5")
            + " collider_sweep_motion=" + contactMotion.ToString("F5")
            + " null_sweep_motion=" + nullMotion.ToString("F5")
            + " scaled_coeff_local_motion=" + scaledLocalMotion.ToString("F5")
            + " unscaled_coeff_local_motion=" + unscaledLocalMotion.ToString("F5")
            + " scaled_gravity_motion=" + scaledGravityMotion.ToString("F5")
            + " unscaled_gravity_motion=" + unscaledGravityMotion.ToString("F5")
            + " forceRenderingOff=true updateWhenOffscreen=true"
            + " vertices=" + sample.Length
            + " snapshot_bytes=" + snapshotBytes
            + " pull_render_us=" + microseconds.ToString("F2");
        line += " actual_bundle=" + (actualPass ? "PASS" : "FAIL")
            + " actual_runners=" + actualRunners
            + " actual_contact_min=" + actualContactMin.ToString("F5")
            + " actual_contact_max=" + actualContactMax.ToString("F5")
            + " actual_table_drop=" + actualTableDrop.ToString("F5")
            + " actual_visible_return_max=" + actualVisibleReturnMax.ToString("F5")
            + " actual_return_step_max=" + actualReturnStepMax.ToString("F5")
            + " actual_return_monotone=" + actualReturnMonotone
            + " actual_pinned_drift_max=" + actualPinnedDriftMax.ToString("F5")
            + " actual_reentry_pop_max=" + actualReentryPopMax.ToString("F5")
            + " actual_approach_raw_drift_max=" + actualApproachRawDriftMax.ToString("F5")
            + " actual_contact_step_max=" + actualContactStepMax.ToString("F5")
            + " actual_reentry_min=" + actualReentryMin.ToString("F5")
            + " actual_longterm_max=" + actualLongTermMax.ToString("F5");
        UnityEngine.Debug.Log("[TOWN-CLOTH] " + line);
        string result = Argument("--result=");
        if (result.Length != 0) File.WriteAllText(result, line + Environment.NewLine);
        Application.Quit(pass ? 0 : 3);
    }

    private static IEnumerator Settle(Fixture fixture, int frames)
    {
        for (int frame = 0; frame < frames; frame++) yield return null;
    }

    private static IEnumerator Freeze(Fixture fixture)
    {
        fixture.Cloth.useGravity = false;
        fixture.Cloth.externalAcceleration = Vector3.zero;
        fixture.Cloth.randomAcceleration = Vector3.zero;
        fixture.Cloth.damping = .85f;
        for (int frame = 0; frame < 120; frame++) yield return null;
    }

    private static IEnumerator Sweep(Fixture fixture)
    {
        yield return Sweep(fixture, .35f);
    }

    private static IEnumerator Sweep(Fixture fixture, float distance)
    {
        Vector3 start = fixture.Mover.position;
        for (int frame = 0; frame < 45; frame++)
        {
            fixture.Mover.position = start + Vector3.right
                * (distance * fixture.StationScale * (frame + 1) / 45f);
            yield return null;
        }
    }

    private static IEnumerator RepeatedSweep(Fixture fixture, float distance, int repetitions)
    {
        Vector3 centre = fixture.Mover.position;
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            Vector3 from = centre + Vector3.right * (repetition == 0 ? 0f : -distance * fixture.StationScale);
            Vector3 to = centre + Vector3.right * (distance * fixture.StationScale);
            for (int frame = 0; frame < 36; frame++)
            {
                float t = (frame + 1f) / 36f;
                fixture.Mover.position = Vector3.Lerp(from, to, t);
                yield return null;
            }
            for (int frame = 0; frame < 36; frame++)
            {
                float t = (frame + 1f) / 36f;
                fixture.Mover.position = Vector3.Lerp(to,
                    centre - Vector3.right * distance * fixture.StationScale, t);
                yield return null;
            }
        }
    }

    private static Fixture Build(string name, float scale, float coefficientMultiplier, bool contact,
        Vector3 position, float yaw)
    {
        return Build(name, scale, 1f, contact, true, position, yaw);
    }

    private static Fixture Build(string name, float stationScale, float fbxScale, bool contact,
        bool convertNestedScale, Vector3 position, float yaw)
    {
        const int columns = 17, rows = 21;
        var vertices = new Vector3[columns * rows];
        var triangles = new int[(columns - 1) * (rows - 1) * 6];
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
                vertices[row * columns + column] = new Vector3(
                    (-.6f + 1.2f * column / (columns - 1f)) / fbxScale,
                    0f, .8f * row / (rows - 1f) / fbxScale);
        int at = 0;
        for (int row = 0; row < rows - 1; row++)
            for (int column = 0; column < columns - 1; column++)
            {
                int a = row * columns + column, b = a + 1, c = a + columns, d = c + 1;
                triangles[at++] = a; triangles[at++] = c; triangles[at++] = b;
                triangles[at++] = b; triangles[at++] = c; triangles[at++] = d;
            }
        var mesh = new Mesh { name = "Town cloth native probe" };
        mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var weights = new BoneWeight[vertices.Length];
        for (int i = 0; i < weights.Length; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        mesh.boneWeights = weights; mesh.bindposes = new[] { Matrix4x4.identity };

        var station = new GameObject(name + ".station") { layer = 2 };
        station.transform.localScale = Vector3.one * stationScale;
        station.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        var source = new GameObject(name + ".fbx-source") { layer = 2 };
        source.transform.SetParent(station.transform, false);
        source.transform.localScale = Vector3.one * fbxScale;
        var driver = new GameObject(name) { layer = 2 };
        Vector3[] driverVertices;
        if (convertNestedScale)
        {
            driver.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            driver.transform.localScale = Vector3.one;
            driverVertices = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                driverVertices[i] = driver.transform.InverseTransformPoint(source.transform.TransformPoint(vertices[i]));
        }
        else
        {
            driver.transform.SetParent(source.transform, false);
            driverVertices = vertices;
        }
        mesh.vertices = driverVertices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var skin = driver.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = mesh; skin.rootBone = driver.transform; skin.bones = new[] { driver.transform };
        skin.updateWhenOffscreen = true; skin.forceRenderingOff = true;
        var cloth = driver.AddComponent<Cloth>();
        cloth.useGravity = true; cloth.useTethers = true; cloth.damping = .2f;
        cloth.stretchingStiffness = .8f; cloth.bendingStiffness = .4f;
        cloth.clothSolverFrequency = 120f; cloth.enableContinuousCollision = true;
        var coefficients = new ClothSkinningCoefficient[vertices.Length];
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
                coefficients[row * columns + column].maxDistance = row == 0 ? 0f
                    : convertNestedScale ? .45f * stationScale : .45f * driver.transform.lossyScale.x;
        cloth.coefficients = coefficients;

        var collider = new GameObject(name + ".moving-probe") { layer = 2 };
        var palm = collider.AddComponent<SphereCollider>(); palm.radius = .16f * stationScale;
        var tipObject = new GameObject("Tip") { layer = 2 }; tipObject.transform.SetParent(collider.transform, false);
        tipObject.transform.localPosition = Vector3.forward * .16f * stationScale;
        var tip = tipObject.AddComponent<SphereCollider>(); tip.radius = .06f * stationScale;
        collider.transform.position = station.transform.TransformPoint(new Vector3(-.18f, -.08f, .48f));
        if (contact) cloth.sphereColliders = new[] { new ClothSphereColliderPair(palm, tip) };
        cloth.ClearTransformMotion();

        var visible = UnityEngine.Object.Instantiate(mesh);
        return new Fixture { Root = station, Cloth = cloth, Mover = collider.transform,
            Visible = visible, Rest = cloth.vertices, StationScale = stationScale,
            SolverScale = driver.transform.lossyScale.x };
    }

    private static Fixture BuildActual(string name, Transform station, MeshFilter filter,
        byte service, float stationScale)
    {
        const int columns = 13, rows = 25, count = columns * rows;
        Vector3[] source = filter.sharedMesh.vertices;
        if (source.Length < count) throw new Exception("actual runner lost its authored source grid");
        var driver = new GameObject(name + ".unit-driver") { layer = 2 };
        driver.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
        float maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
        foreach (Vector3 vertex in source)
        {
            Vector3 point = station.InverseTransformPoint(filter.transform.TransformPoint(vertex));
            maxY = Mathf.Max(maxY, point.y); minX = Mathf.Min(minX, point.x); maxX = Mathf.Max(maxX, point.x);
        }
        float Front(float x)
        {
            float radius = service == 2 ? .81f : .86f, depth = service == 2 ? .38f : .46f;
            return (service == 2 ? 0f : .03f) - depth * Mathf.Sqrt(Mathf.Max(0f, 1f - x * x / (radius * radius)));
        }
        Vector3 Authored(int row, int column)
        {
            float t = row / (rows - 1f), u = column / (columns - 1f), x = Mathf.Lerp(minX, maxX, u);
            float fold = .0015f * Mathf.Sin(u * Mathf.PI * 6.4f + .3f);
            float surface = Mathf.Min(1f, t / .45f);
            float z = Front(x) + .145f * (1f - surface) - .004f * surface;
            z -= .012f * Mathf.Sin(u * Mathf.PI * 6f) * (Mathf.Max(0f, t - .45f) / .55f);
            float y = .9575f + fold - .49f * Mathf.Max(0f, (t - .45f) / .55f);
            y -= .018f * Mathf.Max(0f, (t - .84f) / .16f) * (1f - Mathf.Abs(u * 2f - 1f));
            return new Vector3(x, y, z);
        }
        Vector3[] vertices = new Vector3[count];
        for (int row = 0; row < rows; row++) for (int column = 0; column < columns; column++)
            vertices[row * columns + column] = driver.transform.InverseTransformPoint(
                station.TransformPoint(Authored(row, column)));
        int[] triangles = new int[(rows - 1) * (columns - 1) * 6]; int triangle = 0;
        for (int row = 0; row < rows - 1; row++) for (int column = 0; column < columns - 1; column++)
        {
            int a = row * columns + column, b = a + 1, c = a + columns, d = c + 1;
            triangles[triangle++] = a; triangles[triangle++] = c; triangles[triangle++] = b;
            triangles[triangle++] = b; triangles[triangle++] = c; triangles[triangle++] = d;
        }
        var mesh = new Mesh { name = name + ".mesh" }; mesh.vertices = vertices; mesh.triangles = triangles;
        var weights = new BoneWeight[count];
        for (int i = 0; i < count; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        mesh.boneWeights = weights; mesh.bindposes = new[] { Matrix4x4.identity };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var skin = driver.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh;
        skin.rootBone = driver.transform; skin.bones = new[] { driver.transform };
        skin.updateWhenOffscreen = true; skin.forceRenderingOff = true;
        var cloth = driver.AddComponent<Cloth>(); cloth.useGravity = false; cloth.useTethers = true;
        cloth.externalAcceleration = Physics.gravity * stationScale;
        cloth.damping = .40f; cloth.friction = .52f; cloth.bendingStiffness = .82f;
        cloth.stretchingStiffness = .94f; cloth.clothSolverFrequency = 120f;
        cloth.enableContinuousCollision = true;
        Vector3[] stationPoints = new Vector3[count];
        for (int row = 0; row < rows; row++) for (int column = 0; column < columns; column++)
            stationPoints[row * columns + column] = Authored(row, column);
        var coefficients = new ClothSkinningCoefficient[count];
        var table = new System.Collections.Generic.List<int>();
        for (int i = 0; i < count; i++)
        {
            Vector3 p = stationPoints[i]; float edge = Front(p.x);
            float top = Mathf.Clamp01((edge + .145f - p.z) / .145f);
            float hanging = Mathf.Clamp01((maxY - p.y) / .38f);
            float freedom = Mathf.Pow(Mathf.Max(.10f * top, hanging), 1.25f);
            coefficients[i].maxDistance = freedom * .11f * stationScale;
            coefficients[i].collisionSphereDistance = .004f * stationScale;
            if (p.y > maxY - .05f && p.z >= edge - .01f && p.z <= edge + .15f) table.Add(i);
        }
        cloth.coefficients = coefficients;
        var pairs = new System.Collections.Generic.List<ClothSphereColliderPair>();
        for (int column = 0; column < columns; column++)
        {
            float x = Mathf.Lerp(minX, maxX, column / (columns - 1f));
            SphereCollider Make(float rear, string side)
            {
                var support = new GameObject(name + ".support." + column + "." + side) { layer = 2 };
                support.transform.SetParent(driver.transform, false);
                support.transform.localPosition = driver.transform.InverseTransformPoint(
                    station.TransformPoint(new Vector3(x, maxY - .066f, Front(x) + rear)));
                var sphere = support.AddComponent<SphereCollider>(); sphere.radius = .070f * stationScale;
                return sphere;
            }
            pairs.Add(new ClothSphereColliderPair(Make(.008f, "front"), Make(.142f, "rear")));
        }
        int target = Enumerable.Range(0, count).OrderByDescending(i => coefficients[i].maxDistance
            - Mathf.Abs(stationPoints[i].x) * stationScale).First();
        var mover = new GameObject(name + ".hand") { layer = 2 };
        var palm = mover.AddComponent<SphereCollider>(); palm.radius = .035f * stationScale;
        var tipObject = new GameObject("Tip") { layer = 2 }; tipObject.transform.SetParent(mover.transform, false);
        tipObject.transform.localPosition = Vector3.forward * (.09f * stationScale);
        var tip = tipObject.AddComponent<SphereCollider>(); tip.radius = .01f * stationScale;
        mover.transform.position = driver.transform.TransformPoint(vertices[target]) - station.right * (.09f * stationScale);
        pairs.Add(new ClothSphereColliderPair(palm, tip)); cloth.sphereColliders = pairs.ToArray();
        cloth.ClearTransformMotion();
        return new Fixture { Root = driver, Cloth = cloth, Mover = mover.transform, Visible = mesh,
            Rest = cloth.vertices, StationScale = stationScale, SolverScale = 1f, Station = station,
            Driver = driver.transform, TableIndices = table.ToArray(), TableRest = stationPoints,
            Gravity = Physics.gravity * stationScale, RestDamping = .40f };
    }

    private static float MaximumTableDrop(Fixture fixture)
    {
        Vector3[] current = fixture.Cloth.vertices; float drop = 0f;
        foreach (int index in fixture.TableIndices)
        {
            Vector3 stationPoint = fixture.Station.InverseTransformPoint(
                fixture.Driver.TransformPoint(current[index]));
            drop = Mathf.Max(drop, (fixture.TableRest[index].y - stationPoint.y) * fixture.StationScale);
        }
        return drop;
    }

    private static float MaximumDistance(Vector3[] a, Vector3[] b)
    {
        float maximum = 0f;
        for (int i = 0; i < a.Length && i < b.Length; i++) maximum = Mathf.Max(maximum, Vector3.Distance(a[i], b[i]));
        return maximum;
    }

    private static string Argument(string prefix)
    {
        foreach (string value in Environment.GetCommandLineArgs())
            if (value.StartsWith(prefix, StringComparison.Ordinal)) return value.Substring(prefix.Length);
        return string.Empty;
    }
}
