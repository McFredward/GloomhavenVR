using System;
using System.Diagnostics;
using System.Linq;
using System.Collections;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class TerrainProgram
{
    private static void NativePillarRadius(GameObject host, MeshRenderer source, Camera camera)
    {
        Mesh original = source.GetComponent<MeshFilter>().sharedMesh;
        float scale = source.transform.lossyScale.x;
        Bounds authored = original.bounds;
        Vector3 centre = source.transform.TransformPoint(authored.center);
        float radius = new Vector2(authored.extents.x, authored.extents.z).magnitude * scale;
        PerfConfig.TerrainDetailPercent = PerfConfig.DistantTerrainDetailPercent = 0;
        PerfConfig.CheapWallShadingOn = false; PerfConfig.TerrainDistanceMeters = .75f;
        for (int index = 0; index < 8; index++)
        {
            foreach (float gap in new[] { .5f, .65f, .85f, 1f })
            {
                bool distant = gap > .75f;
                camera.transform.position = centre + Quaternion.Euler(0f, index * 45f, 0f) * Vector3.forward * (radius + gap);
                camera.transform.LookAt(centre); Morph(host);
                Check(ScenarioTerrainBudget.OwnsRenderSubstitute(source) == distant,
                    "native captured pillar uses the configurable viewing radius despite identical zero near and distant caps: " + original.name);
                Color[] baselinePixels = Pixels(camera);
                Check(Visible(baselinePixels) > 10, "native captured pillar radius comparison has nonempty geometry pixels: " + original.name);
                int expectedTriangles = Triangles(distant ? Bank[original][1] : original);
                if (distant)
                    Check(PerfMonitor.Counts["Terrain.SubmittedTriangles"] == expectedTriangles && expectedTriangles < Triangles(original),
                        "native captured distant pillar retains the audited triangle reduction outside its viewing radius: " + original.name);
                PerfConfig.CheapWallShadingOn = true; Tick(host);
                Check(DuringRender(camera, () => source.forceRenderingOff) && PerfMonitor.Counts["Terrain.SubmittedTriangles"] == expectedTriangles,
                    "native pillar viewing radius submits the selected exact or coarse topology when private shading owns the camera: " + original.name);
                Check(SameCoverage(baselinePixels, Pixels(camera)),
                    "native pillar radius preserves exact or coarse orbit camera silhouette through private shading: " + original.name);
                foreach (float gaze in new[] { 35f, 180f })
                {
                    camera.transform.rotation *= Quaternion.Euler(0f, gaze, 0f); Morph(host);
                    PerfConfig.CheapWallShadingOn = false;
                    Check(ScenarioTerrainBudget.OwnsRenderSubstitute(source) == distant,
                        "native captured pillar viewing distance selection remains independent of head yaw: " + original.name);
                    PerfConfig.CheapWallShadingOn = true;
                }
                PerfConfig.CheapWallShadingOn = false;
            }
        }
        // Compare the actual primitive paths with warm, paired, alternating
        // batches. Reflection binds the actual method only once, outside timing.
        IDictionary surfaces = (IDictionary)Driver(host).GetType().GetField("_surfaces", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Driver(host))!;
        object surface = surfaces[source.GetInstanceID()]!;
        var current = (Func<Vector3, Bounds, float>)Delegate.CreateDelegate(typeof(Func<Vector3, Bounds, float>), surface,
            surface.GetType().GetMethod("HeadDistance", BindingFlags.NonPublic | BindingFlags.Instance)!);
        Vector3 head = camera.transform.position;
        var baseline = new double[21]; var fixedTracked = new double[21]; var fixedUntracked = new double[21];
        float sink = 0;
        for (int batch = 0; batch < 21; batch++) for (int order = 0; order < 3; order++)
        {
            int mode = (batch + order) % 3;
            var watch = Stopwatch.StartNew();
            for (int call = 0; call < 512; call++)
            {
                Bounds bounds = mode < 2 ? source.bounds : default;
                sink += mode == 0 ? Vector3.Distance(head, bounds.ClosestPoint(head)) : current(head, bounds);
            }
            watch.Stop(); (mode == 0 ? baseline : mode == 1 ? fixedTracked : fixedUntracked)[batch] = watch.Elapsed.TotalMilliseconds / 512;
        }
        Check(!float.IsNaN(sink), "source-bound pillar paired proximity timing executes actual finite Unity primitive results");
        Array.Sort(baseline); Array.Sort(fixedTracked); Array.Sort(fixedUntracked);
        UnityEngine.Debug.Log("Pillar paired proximity ms/source " + original.name + ": old=" + baseline[10]
            + ", tracked=" + fixedTracked[10] + ", untracked=" + fixedUntracked[10] + "; 21 paired batches/512 calls; software GL, not Frame timing.");
    }

    private static void RadialPillars(Camera camera)
    {
        ProceduralWall.m_WallCache.Clear();
        var host = new GameObject("GloomhavenVR.RadialTerrain");
        var scenario = new GameObject("Radial scenario"); scenario.AddComponent<ProceduralScenario>();
        var material = new Material(Shader.Find("Amp_Basic_N_MRAO")); material.SetColor("_Tint", Color.white);
        var source = Surface(scenario, "CV_Pillar_Generic_01", Vector3.zero, material);
        Mesh original = source.GetComponent<MeshFilter>().sharedMesh;
        PerfConfig.CheapWallShadingOn = false; PerfConfig.TerrainDetailPercent = PerfConfig.DistantTerrainDetailPercent = 0;
        PerfConfig.TerrainCameraSourceLimit = 0; PerfConfig.SharedEnvironmentMaterialReadsOn = true;
        GloomhavenVR.Rig.VRRigDriver.HeadCamera = camera;
        camera.transform.localScale = Vector3.one; camera.orthographicSize = 1.5f;
        ScenarioTerrainBudget.ConfigureAssetPreparation(() => true, () => false);
        ScenarioTerrainBudget.ConfigureMeshBank(Bank.ContainsKey, Lookup);
        ScenarioTerrainBudget.ConfigureCanonicalMaterial(current => current);
        ScenarioTerrainBudget.ConfigureNativeCameraConsumers(current => current.commandBufferCount > 0);
        ScenarioTerrainBudget.Install(host); ScenarioTerrainBudget.QueueRoot(scenario);
        try
        {
            // Same physical radius: the old AABB is 0.857m away on axis and
            // 0.650m away on the diagonal. The configurable .75m viewing radius
            // must restore the near geometry on both. Settle each morph first.
            float radius = Mathf.Sqrt(.5f); PerfConfig.TerrainDistanceMeters = .75f;
            camera.transform.position = new Vector3(0f, 0f, -4f); Morph(host);
            Check(ScenarioTerrainBudget.OwnsRenderSubstitute(source), "far radial pillar still uses the configured coarse geometry");
            foreach (float angle in new[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f })
            {
                camera.transform.position = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (radius + .65f);
                camera.transform.LookAt(Vector3.zero); Morph(host);
                Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(source), "fixed-radius pillar head proximity restores exact geometry at every orbit angle");
                foreach (float gaze in new[] { 0f, 35f, 180f })
                {
                    camera.transform.rotation *= Quaternion.Euler(0f, gaze, 0f); Morph(host);
                    Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(source), "head yaw alone cannot change settled nearby pillar detail");
                }
            }
            camera.transform.position = Vector3.forward * (radius + .85f); Morph(host);
            Check(ScenarioTerrainBudget.OwnsRenderSubstitute(source), "radial pillar outside the configured viewing radius retains the configured coarse endpoint");
            foreach (var edge in new[] { (.65f, false), (.77f, false), (.8f, true), (.73f, true), (.70f, false) })
            {
                camera.transform.position = Vector3.forward * (radius + edge.Item1); Morph(host);
                Check(ScenarioTerrainBudget.OwnsRenderSubstitute(source) == edge.Item2,
                    "pillar viewing radius retains separate entry and exit edges without boundary chatter");
            }
            camera.transform.position = Vector3.forward * (radius + .5f);
            foreach (float live in new[] { .2f, .9f, .2f, .75f })
            {
                PerfConfig.TerrainDistanceMeters = live; Morph(host);
                Check(ScenarioTerrainBudget.OwnsRenderSubstitute(source) == (live < .5f),
                    "live pillar viewing radius edits change settled detail even with identical zero caps");
            }
            scenario.transform.localScale = camera.transform.localScale = Vector3.one * 2f;
            foreach (float gap in new[] { .65f, .85f })
            {
                camera.transform.position = Vector3.forward * ((radius + gap) * 2f); Morph(host);
                Check(ScenarioTerrainBudget.OwnsRenderSubstitute(source) == (gap > .75f),
                    "scaled board and headset preserve the same physical pillar viewing radius");
            }
            scenario.transform.localScale = camera.transform.localScale = Vector3.one;
            PerfConfig.TerrainDetailPercent = 100; PerfConfig.DistantTerrainDetailPercent = 0; PerfConfig.TerrainDistanceMeters = .75f;
            foreach (float gap in new[] { .65f, .85f }) for (int index = 0; index < 8; index++)
            {
                camera.transform.position = Quaternion.Euler(0f, index * 45f, 0f) * Vector3.forward * (radius + gap); Morph(host);
                Check(ScenarioTerrainBudget.OwnsRenderSubstitute(source) == (gap > .75f), "configured pillar near and distant geometry are invariant under a fixed-radius orbit");
            }
            PerfConfig.TerrainDetailPercent = PerfConfig.DistantTerrainDetailPercent = 0;
            Mesh exact = Bank[original][0], coarse = Bank[original][1];
            var shiftedExact = Object.Instantiate(exact); var shiftedCoarse = Object.Instantiate(coarse); Vector3 shift = new(.4f, -.8f, .3f);
            shiftedExact.vertices = exact.vertices.Select(vertex => vertex + shift).ToArray(); shiftedExact.RecalculateBounds();
            shiftedCoarse.vertices = coarse.vertices.Select(vertex => vertex + shift).ToArray(); shiftedCoarse.RecalculateBounds();
            var shiftedOriginal = Object.Instantiate(shiftedExact); shiftedOriginal.name = "CV_Pillar_Generic_01"; shiftedOriginal.UploadMeshData(true);
            Bank.Add(shiftedOriginal, new[] { shiftedExact, shiftedCoarse }); source.GetComponent<MeshFilter>().sharedMesh = shiftedOriginal;
            Quaternion tilt = Quaternion.Euler(31f, 42f, 17f);
            scenario.transform.SetPositionAndRotation(new Vector3(3f, -1f, 2f), tilt); scenario.transform.localScale = new Vector3(2f, 1.5f, .6f);
            ScenarioTerrainBudget.QueueRoot(source.gameObject); Vector3 centre = source.transform.TransformPoint(shift);
            float stretchedRadius = Mathf.Sqrt(1f + .09f);
            for (int index = 0; index < 8; index++)
            {
                camera.transform.position = centre + tilt * Quaternion.Euler(0f, index * 45f, 0f) * Vector3.forward * (stretchedRadius + .65f);
                camera.transform.LookAt(centre); Morph(host);
                Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(source), "tilted nonuniform off-centre pillar retains exact geometry throughout a constant-radius orbit");
            }
            for (int index = 0; index < 8; index++)
            {
                camera.transform.position = centre + tilt * Quaternion.Euler(0f, index * 45f, 0f) * Vector3.forward * (stretchedRadius + .85f);
                camera.transform.LookAt(centre); Morph(host);
                Check(ScenarioTerrainBudget.OwnsRenderSubstitute(source), "tilted pillar outside its source-axis cylinder retains configured coarse geometry at every orbit angle");
            }
            TerrainReadObserver.Reset(); Tick(host);
            Check(TerrainReadObserver.SourceMatrices == 1 && TerrainReadObserver.BoundsReads == 0, "untracked pillar head distance replaces its native bounds read with one current Update matrix read");
            var hand = new GameObject("Radial hand").AddComponent<GloomhavenVR.Hands.VRHand>(); hand.HasPose = true; hand.transform.position = centre + Vector3.one * 100f;
            GloomhavenVR.Hands.VRHands.Left = hand; TerrainReadObserver.Reset(); Tick(host);
            Check(TerrainReadObserver.SourceMatrices == 1 && TerrainReadObserver.BoundsReads == 1, "tracked pillar touch retains one unchanged current native bounds read alongside radial head proximity");
            GloomhavenVR.Hands.VRHands.Left = null; Object.DestroyImmediate(hand.gameObject);
            scenario.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); scenario.transform.localScale = Vector3.one;
            source.GetComponent<MeshFilter>().sharedMesh = original; ScenarioTerrainBudget.QueueRoot(source.gameObject);
            camera.transform.position = new Vector3(0f, 0f, -(radius + .65f)); camera.transform.LookAt(Vector3.zero);
            var costly = Surface(scenario, "CV_Wall_Generic_01", new Vector3(0f, 0f, 1f), material);
            Mesh oldCostly = costly.GetComponent<MeshFilter>().sharedMesh; Mesh[] costlyBank = Bank[oldCostly];
            Mesh costlyExact = Object.Instantiate(costlyBank[0]); costlyExact.triangles = costlyExact.triangles.Concat(costlyExact.triangles).ToArray();
            Mesh costlyCoarse = Coarse(costlyExact); Mesh costlyOriginal = Object.Instantiate(costlyExact); costlyOriginal.name = "CV_Wall_Generic_01"; costlyOriginal.UploadMeshData(true);
            costly.GetComponent<MeshFilter>().sharedMesh = costlyOriginal; Bank.Add(costlyOriginal, new[] { costlyExact, costlyCoarse });
            PerfConfig.CheapWallShadingOn = true; PerfConfig.TerrainCameraSourceLimit = 1; ScenarioTerrainBudget.QueueRoot(costly.gameObject); Morph(host);
            Check(DuringRender(camera, () => !source.forceRenderingOff && costly.forceRenderingOff) && PerfMonitor.Counts["Terrain.CameraCandidates"] == 1
                && PerfMonitor.Counts["ScenarioTerrain.BudgetDeferred"] == 1, "nearby late pillar stays exact through native cap fallback without increasing per-eye source work");
            Color[] deferred = Pixels(camera); PerfConfig.TerrainCameraSourceLimit = 0;
            Check(Visible(deferred) > 10, "nearby pillar cap fallback pixel comparison has nonempty native camera coverage");
            Check(DuringRender(camera, () => source.forceRenderingOff), "unlimited eye admits the same nearby exact pillar to its private shader route");
            Check(SameCoverage(deferred, Pixels(camera)), "near pillar native budget fallback and admitted private endpoint have identical camera silhouette pixels");
            source.enabled = false; Check(!DuringRender(camera, () => source.forceRenderingOff), "radial correction never revives a natively disabled pillar"); source.enabled = true;
            source.forceRenderingOff = true; Render(camera); Check(source.forceRenderingOff, "radial correction never recovers a foreign pillar mask"); source.forceRenderingOff = false;
            var watch = Stopwatch.StartNew(); for (int index = 0; index < 1000; index++) Tick(host); watch.Stop();
            UnityEngine.Debug.Log("Pillar radial two-source real Unity Update: " + watch.Elapsed.TotalMilliseconds / 1000d + "ms/call (diagnostic).");
        }
        finally
        {
            ScenarioTerrainBudget.Shutdown(); Object.DestroyImmediate(host); Object.DestroyImmediate(scenario); Object.DestroyImmediate(material); ProceduralWall.m_WallCache.Clear();
            PerfConfig.CheapWallShadingOn = false; PerfConfig.TerrainCameraSourceLimit = 0; PerfConfig.TerrainDetailPercent = PerfConfig.DistantTerrainDetailPercent = 100; PerfConfig.TerrainDistanceMeters = .75f; camera.transform.localScale = Vector3.one;
        }
    }
}
