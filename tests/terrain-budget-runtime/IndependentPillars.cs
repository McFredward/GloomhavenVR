using System;
using System.Collections;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class TerrainProgram
{
    private delegate float RadiusDistance(Vector3 head, Bounds bounds, bool measure, out float radius);
    private static object IndependentSurface(GameObject host, MeshRenderer source)
    {
        IDictionary surfaces = (IDictionary)Driver(host).GetType().GetField("_surfaces", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Driver(host))!;
        return surfaces[source.GetInstanceID()]!;
    }
    private static RadiusDistance IndependentDistance(object surface) => (RadiusDistance)Delegate.CreateDelegate(typeof(RadiusDistance), surface,
        surface.GetType().GetMethod("HeadDistanceAndRadius", BindingFlags.NonPublic | BindingFlags.Instance)!);

    private static void NativeIndependentPillarRadius(GameObject host, MeshRenderer source, Camera camera)
    {
        Mesh original = source.GetComponent<MeshFilter>().sharedMesh;
        Bounds authored = original.bounds;
        Transform scenario = source.transform.parent.parent.parent;
        Vector3 oldScenarioScale = scenario.localScale;
        Vector3 oldHeadScale = camera.transform.localScale;
        float authoredRadial = new Vector2(authored.extents.x, authored.extents.z).magnitude;
        PerfConfig.PillarDistanceLodEnabled = true; PerfConfig.FigureDistanceLodEnabled = true;
        PerfConfig.CheapWallShadingOn = false;
        PerfConfig.DistantTerrainDetailPercent = 0;
        PerfConfig.TerrainDistanceMeters = .1f;
        try
        {
            foreach (float scale in new[] { 1f, 2f })
            {
                scenario.localScale = oldScenarioScale * scale;
                camera.transform.localScale = Vector3.one * scale;
                Vector3 centre = source.transform.TransformPoint(authored.center);
                float sourceScale = source.transform.lossyScale.x;
                float radial = authoredRadial * sourceScale;
                // Independent oracle: full original authored extents, never current
                // renderer bounds or any actual figure/party's size or state.
                float worldRadius = authored.extents.magnitude * sourceScale;
                var referenceBounds = new Bounds(Vector3.zero, new Vector3(worldRadius * 2f, 0f, 0f));
                foreach (int cap in new[] { 0, 66, 67, 100 })
                {
                    PerfConfig.TerrainDetailPercent = cap;
                    Vector2 edges = FigureDistanceLodPolicy.FirstReductionDistances(cap, worldRadius) / scale;
                    float span = edges.x - edges.y;
                    for (int orbit = 0; orbit < (scale == 1f ? 8 : 1); orbit++)
                    {
                        var selector = new FigureDistanceLodPolicy();
                        foreach (float gap in new[] { edges.y - span * .05f, edges.x + span * .05f, (edges.x + edges.y) * .5f, edges.y - span * .05f })
                        {
                            bool reduced = FigureEffectiveTier.Selected(selector.Select(cap, referenceBounds,
                                Vector3.forward * gap * scale, false)) < FigureEffectiveTier.Selected(cap);
                            camera.transform.position = centre + Quaternion.Euler(0f, orbit * 45f, 0f) * Vector3.forward * (radial + gap * scale);
                            camera.transform.LookAt(centre); Morph(host);
                            Check(ScenarioTerrainBudget.OwnsRenderSubstitute(source) == reduced,
                                "independent native pillar first reduction matches its own original full radius and shared figure tier bands for cap " + cap + ": " + original.name);
                            camera.transform.rotation *= Quaternion.Euler(0f, 37f, 0f); Morph(host);
                            Check(ScenarioTerrainBudget.OwnsRenderSubstitute(source) == reduced,
                                "independent pillar selection remains invariant under radial orbit and head yaw: " + original.name);
                            camera.transform.LookAt(centre);
                            if (orbit != 0) continue;
                            Color[] normal = Pixels(camera);
                            Check(Visible(normal) > 10, "independent native pillar comparison has nonempty exact or reduced geometry pixels: " + original.name);
                            int expected = Triangles(reduced ? Bank[original][1] : original);
                            PerfConfig.CheapWallShadingOn = true; Tick(host);
                            Check(DuringRender(camera, () => source.forceRenderingOff)
                                && PerfMonitor.Counts["Terrain.SubmittedTriangles"] == expected,
                                "independent native pillar camera submits its selected exact or coarse original topology: " + original.name);
                            Check(!reduced || expected < Triangles(original), "independent far pillar endpoint retains an actual original triangle saving: " + original.name);
                            Check(SameCoverage(normal, Pixels(camera)), "independent native and private shading preserve the same selected pillar silhouette: " + original.name);
                            PerfConfig.CheapWallShadingOn = false;
                        }
                    }
                }
                // The pillar's own cap and transform are the only live size inputs.
                camera.transform.position = centre + Vector3.forward * (radial + worldRadius * 10f);
                PerfConfig.TerrainDetailPercent = 100; Morph(host);
                Check(ScenarioTerrainBudget.OwnsRenderSubstitute(source), "live high pillar near cap uses its own effective middle distance band");
                PerfConfig.TerrainDetailPercent = 0; Morph(host);
                Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(source), "live zero pillar near cap uses its own effective far band instead of an ineffective middle tier");
                camera.transform.position = centre + Vector3.forward * (radial + worldRadius * 22f); Morph(host);
                Check(ScenarioTerrainBudget.OwnsRenderSubstitute(source), "independent pillar remains reducible at its own genuine distant endpoint");
                camera.transform.position = centre + Vector3.forward * (radial + worldRadius * 10f); Morph(host);
                Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(source), "figure-toggle independence fixture is within the original pillar near band and outside the manual radius");
                foreach (bool figureLod in new[] { false, true })
                {
                    PerfConfig.FigureDistanceLodEnabled = figureLod; Morph(host);
                    Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(source), "figure distance toggle cannot change independently selected pillar detail");
                }
                TerrainReadObserver.Reset(); Tick(host);
                Check(TerrainReadObserver.SourceMatrices == 1 && TerrainReadObserver.BoundsReads == 0,
                    "independent pillar uses one Update source matrix without an additional native bounds read");
                TerrainReadObserver.Reset(); Render(camera); Render(camera);
                Check(TerrainReadObserver.SourceMatrices == 0 && TerrainReadObserver.BoundsReads == 0,
                    "independent pillar geometry is not reread during paired camera callbacks");
            }
        }
        finally
        {
            scenario.localScale = oldScenarioScale; camera.transform.localScale = oldHeadScale;
            PerfConfig.PillarDistanceLodEnabled = false; PerfConfig.FigureDistanceLodEnabled = true;
            PerfConfig.CheapWallShadingOn = false; PerfConfig.TerrainDistanceMeters = .75f;
        }
    }

    private static void IndependentPillarLifecycle(Camera camera)
    {
        ProceduralWall.m_WallCache.Clear();
        var host = new GameObject("GloomhavenVR.IndependentPillars");
        var scenario = new GameObject("Independent scenario"); scenario.AddComponent<ProceduralScenario>();
        var material = new Material(Shader.Find("Amp_Basic_N_MRAO")); material.SetColor("_Tint", Color.white);
        var pillar = Surface(scenario, "CV_Pillar_Generic_01", Vector3.zero, material);
        var wall = Surface(scenario, "CV_Wall_Generic_01", Vector3.right * 3f, material);
        float radial = Mathf.Sqrt(.5f), originalRadius = Mathf.Sqrt(.75f);
        camera.transform.localScale = Vector3.one;
        camera.transform.position = Vector3.forward * (radial + originalRadius * 21f); camera.transform.LookAt(Vector3.zero);
        PerfConfig.PillarDistanceLodEnabled = true; PerfConfig.FigureDistanceLodEnabled = true;
        PerfConfig.CheapWallShadingOn = false; PerfConfig.TerrainSubstitutionOn = true; PerfConfig.TerrainCameraSourceLimit = 0;
        PerfConfig.TerrainDetailPercent = PerfConfig.DistantTerrainDetailPercent = 0; PerfConfig.TerrainDistanceMeters = .75f;
        ScenarioTerrainBudget.ConfigureMeshBank(Bank.ContainsKey, Lookup);
        ScenarioTerrainBudget.ConfigureAssetPreparation(() => true, () => false);
        ScenarioTerrainBudget.ConfigurePerformanceWallVisibility(_ => false);
        ScenarioTerrainBudget.Install(host); ScenarioTerrainBudget.QueueRoot(scenario);
        GameObject? actor = null;
        try
        {
            Morph(host);
            Check(ScenarioTerrainBudget.OwnsRenderSubstitute(pillar), "independent pillar selects distant detail in a scene with zero figures");
            actor = new GameObject("Unrelated actor"); actor.AddComponent<ActorBehaviour>(); Morph(host);
            Check(ScenarioTerrainBudget.OwnsRenderSubstitute(pillar), "unrelated figure presence cannot change a pillar's own camera distance band");
            Object.DestroyImmediate(actor); actor = null; Morph(host);
            Check(ScenarioTerrainBudget.OwnsRenderSubstitute(pillar), "figure removal cannot invalidate independent pillar detail");
            Check(ScenarioTerrainBudget.OwnsRenderSubstitute(wall), "independent pillar size policy leaves ordinary wall near and far settings unchanged");
            camera.transform.position = Vector3.forward * (radial + originalRadius * 10f);
            Morph(host); Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(pillar), "independent pillar original near geometry works even with identical zero caps");
            object surface = IndependentSurface(host, pillar);
            RadiusDistance metric = IndependentDistance(surface);
            foreach (Vector3 scale in new[] { Vector3.one, Vector3.one * 2f, new Vector3(2f, 1.5f, .6f) })
            {
                scenario.transform.localScale = scale;
                float expected = (Vector3.Scale(Vector3.one * .5f, scale)).magnitude;
                foreach (Vector3 angles in new[] { Vector3.zero, new Vector3(0f, 45f, 0f), new Vector3(31f, 42f, 17f) })
                {
                    scenario.transform.rotation = Quaternion.Euler(angles);
                    metric(camera.transform.position, default, true, out float measured);
                    Check(Mathf.Abs(measured - expected) < .00001f,
                        "own original pillar full radius is invariant under source rotation and respects nonuniform native scale");
                }
            }
            scenario.transform.localScale = Vector3.one; scenario.transform.rotation = Quaternion.identity;

            // Exact equality uses a source-scaled radius whose binary-representable
            // 8/7 bands land on exact cylinder distances, without decimal rounding.
            pillar.transform.localScale = Vector3.one / Mathf.Sqrt(.75f);
            metric(Vector3.zero, default, true, out float equalityRadius);
            Vector2 edges = FigureDistanceLodPolicy.FirstReductionDistances(100, equalityRadius);
            float equalityRadial = radial * pillar.transform.localScale.x;
            PerfConfig.TerrainDetailPercent = 100;
            camera.transform.position = Vector3.forward * (equalityRadial + edges.y - .5f); Morph(host);
            camera.transform.position = Vector3.forward * (equalityRadial + edges.x);
            float entryObserved = metric(camera.transform.position, default, true, out _);
            Check(entryObserved == edges.x, "independent pillar entry equality fixture binds exact production cylinder float distance");
            Morph(host); Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(pillar), "independent pillar entry equality retains its original near band");
            camera.transform.position += Vector3.forward * .5f; Morph(host);
            Check(ScenarioTerrainBudget.OwnsRenderSubstitute(pillar), "strict independent pillar entry crossing selects the coarse endpoint");
            camera.transform.position = Vector3.forward * (equalityRadial + edges.y);
            Check(metric(camera.transform.position, default, true, out _) == edges.y, "independent pillar exit equality fixture binds exact production cylinder float distance");
            Morph(host); Check(ScenarioTerrainBudget.OwnsRenderSubstitute(pillar), "independent pillar exit equality retains its existing distant band");
            camera.transform.position -= Vector3.forward * .5f; Morph(host);
            Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(pillar), "strict independent pillar exit crossing restores original geometry");
            pillar.transform.localScale = Vector3.one;

            camera.transform.position = Vector3.forward * (radial + 1f); PerfConfig.TerrainDetailPercent = 0;
            foreach (bool independent in new[] { false, true, false, true })
            {
                PerfConfig.PillarDistanceLodEnabled = independent; Morph(host);
                Check(ScenarioTerrainBudget.OwnsRenderSubstitute(pillar) == !independent,
                    "live independent pillar toggle preserves the configurable saved manual viewing radius");
            }
            // A malformed captured original size must not force a guessed tiny band.
            FieldInfo authoredBounds = surface.GetType().GetField("_originalBounds", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Bounds savedBounds = (Bounds)authoredBounds.GetValue(surface)!;
            foreach (float invalid in new[] { 0f, float.NaN, float.PositiveInfinity })
            {
                authoredBounds.SetValue(surface, new Bounds(Vector3.zero, Vector3.one * invalid)); Morph(host);
                Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(pillar), "invalid original pillar size retains exact geometry and clears its own far latch");
            }
            authoredBounds.SetValue(surface, savedBounds);
            camera.transform.position = Vector3.forward * (radial + originalRadius * 21f); Morph(host);
            Check(ScenarioTerrainBudget.OwnsRenderSubstitute(pillar), "degenerate scale recovery fixture begins with a genuinely reduced independent pillar");
            pillar.transform.localScale = new Vector3(1f, 0f, 1f); Morph(host);
            Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(pillar), "zero native pillar height retains exact geometry instead of borrowing a current AABB radius");
            pillar.transform.localScale = Vector3.one;
            camera.transform.position = Vector3.forward * (radial + 1f);
            TerrainReadObserver.Reset(); Tick(host);
            Check(TerrainReadObserver.HeadPositions == 1 && TerrainReadObserver.SourceMatrices == 1 && TerrainReadObserver.BoundsReads == 1,
                "mixed wall and independent pillar sources share the original one head snapshot and current source reads");
            ScenarioTerrainBudget.ConfigurePerformanceWallVisibility(renderer => renderer == pillar || renderer == wall);
            TerrainReadObserver.Reset(); Tick(host); Render(camera);
            Check(TerrainReadObserver.SourceMatrices == 0 && TerrainReadObserver.HeadPositions == 0,
                "exact hidden independent pillars perform zero head matrix radius or camera maintenance");
            ScenarioTerrainBudget.ConfigurePerformanceWallVisibility(_ => false);
            pillar.enabled = false; Morph(host);
            Check(!DuringRender(camera, () => pillar.forceRenderingOff), "independent pillar size policy never revives a natively disabled renderer");
        }
        finally
        {
            if (actor != null) Object.DestroyImmediate(actor);
            ScenarioTerrainBudget.ConfigurePerformanceWallVisibility(_ => false); ScenarioTerrainBudget.Shutdown();
            Object.DestroyImmediate(host); Object.DestroyImmediate(scenario); Object.DestroyImmediate(material); ProceduralWall.m_WallCache.Clear();
            PerfConfig.PillarDistanceLodEnabled = false; PerfConfig.FigureDistanceLodEnabled = true;
            PerfConfig.CheapWallShadingOn = false; PerfConfig.TerrainDetailPercent = PerfConfig.DistantTerrainDetailPercent = 100;
            PerfConfig.TerrainDistanceMeters = .75f;
        }
    }
}
