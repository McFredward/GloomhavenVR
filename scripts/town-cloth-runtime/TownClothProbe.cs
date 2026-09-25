using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
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

        Fixture scaled = Build("authored-coefficients", 1f, 1f, true,
            new Vector3(3f, 2f, -4f), 37f);
        yield return Settle(scaled, 60);
        yield return Freeze(scaled);
        Vector3[] scaledBefore = scaled.Cloth.vertices;
        yield return Sweep(scaled, .60f);
        float scaledLocalMotion = MaximumDistance(scaledBefore, scaled.Cloth.vertices);

        Fixture unscaled = Build("underscaled-coefficients-negative-control", 1f, .001f, true,
            new Vector3(-3f, 2f, -4f), -29f);
        yield return Settle(unscaled, 60);
        yield return Freeze(unscaled);
        Vector3[] unscaledBefore = unscaled.Cloth.vertices;
        yield return Sweep(unscaled, .60f);
        float unscaledLocalMotion = MaximumDistance(unscaledBefore, unscaled.Cloth.vertices);

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
        // Cloth coefficients are world distances while Cloth.vertices is local.
        // At transform scale 10, scaling the coefficient preserves the authored
        // local travel; omitting it limits travel to roughly one tenth.
        bool scalePass = scaledLocalMotion > .025f
            && scaledLocalMotion > unscaledLocalMotion * 1.25f
            && scaledLocalMotion < .75f;
        bool pass = hiddenPass && negativePass && scalePass;
        string line = "native_hidden_cloth=" + (pass ? "PASS" : "FAIL")
            + " gravity_motion=" + gravityMotion.ToString("F5")
            + " collider_sweep_motion=" + contactMotion.ToString("F5")
            + " null_sweep_motion=" + nullMotion.ToString("F5")
            + " scaled_coeff_local_motion=" + scaledLocalMotion.ToString("F5")
            + " unscaled_coeff_local_motion=" + unscaledLocalMotion.ToString("F5")
            + " forceRenderingOff=true updateWhenOffscreen=true"
            + " vertices=" + sample.Length
            + " snapshot_bytes=" + snapshotBytes
            + " pull_render_us=" + microseconds.ToString("F2");
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
            fixture.Mover.position = start + Vector3.right * (distance * (frame + 1) / 45f);
            yield return null;
        }
    }

    private static Fixture Build(string name, float scale, float coefficientMultiplier, bool contact,
        Vector3 position, float yaw)
    {
        const int columns = 17, rows = 21;
        var vertices = new Vector3[columns * rows];
        var triangles = new int[(columns - 1) * (rows - 1) * 6];
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
                vertices[row * columns + column] = new Vector3(-.6f + 1.2f * column / (columns - 1f),
                    0f, .8f * row / (rows - 1f));
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

        var driver = new GameObject(name) { layer = 2 };
        driver.transform.localScale = Vector3.one * scale;
        driver.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
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
                coefficients[row * columns + column].maxDistance = row == 0 ? 0f : .45f * coefficientMultiplier;
        cloth.coefficients = coefficients;

        var collider = new GameObject(name + ".moving-probe") { layer = 2 };
        var palm = collider.AddComponent<SphereCollider>(); palm.radius = .16f * scale;
        var tipObject = new GameObject("Tip") { layer = 2 }; tipObject.transform.SetParent(collider.transform, false);
        tipObject.transform.localPosition = Vector3.forward * .16f * scale;
        var tip = tipObject.AddComponent<SphereCollider>(); tip.radius = .06f * scale;
        collider.transform.position = driver.transform.TransformPoint(new Vector3(-.18f, -.08f, .48f));
        if (contact) cloth.sphereColliders = new[] { new ClothSphereColliderPair(palm, tip) };
        cloth.ClearTransformMotion();

        var visible = UnityEngine.Object.Instantiate(mesh);
        return new Fixture { Root = driver, Cloth = cloth, Mover = collider.transform,
            Visible = visible, Rest = cloth.vertices };
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
