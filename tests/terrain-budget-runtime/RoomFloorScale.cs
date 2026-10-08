using System;
using System.Diagnostics;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class TerrainProgram
{
    private static void RoomFloorScale(Camera camera)
    {
        const int count = 256;
        var host = new GameObject("GloomhavenVR.RoomFloorScale");
        var scenario = new GameObject("Scenario"); scenario.AddComponent<ProceduralScenario>();
        var material = new Material(Shader.Find("Amp_Basic_N_MRAO")); material.SetColor("_Tint", Color.white);
        MeshRenderer first = Surface(scenario, "VerifiedScaleFloor", Vector3.up, material, false);
        Mesh original = first.GetComponent<MeshFilter>().sharedMesh;
        var floors = new List<MeshRenderer> { first };
        Transform parent = first.transform.parent;
        for (int index = 1; index < count; index++)
        {
            var obj = new GameObject("RepeatedFloor"); obj.transform.SetParent(parent, false);
            obj.transform.position = new Vector3((index % 16 - 7.5f) * .11f, 1f, (index / 16 - 7.5f) * .11f);
            obj.transform.localScale = Vector3.one * .1f;
            obj.AddComponent<MeshFilter>().sharedMesh = original;
            var renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            // Half the floors carry live material-index blocks, forcing the native
            // property-copy path rather than testing only empty blocks/groups.
            if ((index & 1) != 0)
            {
                var block = new MaterialPropertyBlock(); block.SetColor("_Tint", new Color(.8f, .4f, .2f, 1f));
                renderer.SetPropertyBlock(block, 0);
            }
            floors.Add(renderer);
        }
        first.transform.position = new Vector3(-.825f, 1f, -.825f); first.transform.localScale = Vector3.one * .1f;
        MeshRenderer wall = Surface(scenario, "CV_Wall_Generic_01", new Vector3(0, 1, 0), material);
        ScenarioTerrainBudget.ConfigureRoomArchitecture(() => true, () => 0);
        int roleReads = 0;
        ScenarioTerrainBudget.ConfigureArchitectureBank(mesh => { roleReads++; return mesh == original ? 1 : 0; });
        ScenarioTerrainBudget.ConfigureRoomFloorCameraSourceLimit(() => 0);
        ScenarioTerrainBudget.ConfigureFloorGrouping(renderer => false);
        int notifications = 0;
        ScenarioTerrainBudget.ConfigureFloorMeshReady(renderer => notifications++);
        ScenarioTerrainBudget.ConfigureMeshBank(Bank.ContainsKey, Lookup);
        ScenarioTerrainBudget.ConfigureAssetPreparation(() => true, () => false);
        ScenarioTerrainBudget.ConfigureWorldMaterialIntegration(current => current, () => new MaterialPass(), () => false, current => false);
        PerfConfig.CheapWallShadingOn = false; PerfConfig.TerrainSubstitutionOn = true;
        PerfConfig.TerrainDetailPercent = 0; PerfConfig.DistantTerrainDetailPercent = 0;
        PerfConfig.SharedEnvironmentMaterialReadsOn = true; PerfConfig.TerrainCameraSourceLimit = 1;
        camera.transform.position = new Vector3(0, 3, -3); camera.transform.LookAt(Vector3.up); camera.orthographicSize = 2f;
        ScenarioTerrainBudget.Install(host); ScenarioTerrainBudget.QueueRoot(scenario); Morph(host, 48);
        Check(DuringRender(camera, () => floors.TrueForAll(renderer => renderer.forceRenderingOff) && wall.forceRenderingOff),
            "separate unlimited room floor budget leases256 live floors without starving the one-source structural budget");
        Check(PerfMonitor.Counts["Terrain.FloorSubstitutes"] == count && PerfMonitor.Counts["Terrain.StructuralSubstitutes"] == 1
            && PerfMonitor.Counts["Terrain.FloorCameraCandidates"] == count,
            "large repeated room coverage counters measure256 surviving floor leases and one surviving wall independently");
        Check(PerfMonitor.Counts["Terrain.SubmittedTriangles"] < PerfMonitor.Counts["Terrain.OriginalTriangles"] / 2,
            "large repeated floor and wall coverage materially reduce actual paired submitted triangles");
        int settled = notifications; Morph(host, 4);
        Check(settled == count && notifications == settled,
            "settled room floor readiness reports one endpoint revision per floor and no per-frame rebuild notifications");
        roleReads = 0;
        using (ScenarioTerrainBudget.BeginFloorReadPass())
            foreach (MeshRenderer floor in floors)
                Check(ScenarioTerrainBudget.TryGetSettledRoomFloor(floor, out _), "current floor read pass exposes every prepared endpoint");
        Check(roleReads == 1, "one native floor read pass validates one repeated original bank signature rather than256 copies");
        using (ScenarioTerrainBudget.BeginFloorReadPass())
        {
            Check(ScenarioTerrainBudget.TryGetSettledRoomFloor(first, out _), "outer floor read pass starts with fresh original metadata");
            using (ScenarioTerrainBudget.BeginFloorReadPass())
                Check(ScenarioTerrainBudget.TryGetSettledRoomFloor(first, out _), "nested floor read pass validates fresh original metadata");
            Check(ScenarioTerrainBudget.TryGetSettledRoomFloor(first, out _), "resumed outer floor read pass validates fresh original metadata");
            ScenarioTerrainBudget.BeforeNativeRendererWrite(first);
            Check(ScenarioTerrainBudget.TryGetSettledRoomFloor(first, out _), "native floor writer invalidates same-pass original metadata");
        }
        Check(roleReads == 5, "nested pass and native writer invalidate repeated floor bank metadata without crossing frames");
        var watch = Stopwatch.StartNew();
        for (int frame = 0; frame < 10; frame++) Render(camera);
        watch.Stop();
        UnityEngine.Debug.Log("Room floor scale: sources=" + count + ", leased=" + PerfMonitor.Counts["Terrain.FloorSubstitutes"]
            + ", originalTriangles=" + PerfMonitor.Counts["Terrain.OriginalTriangles"]
            + ", submittedTriangles=" + PerfMonitor.Counts["Terrain.SubmittedTriangles"]
            + ", actualCameraMeanMs=" + watch.Elapsed.TotalMilliseconds / 10
            + "; llvmpipe editor render timing, not headset FPS.");
        ScenarioTerrainBudget.ConfigureRoomFloorCameraSourceLimit(() => 3);
        Check(DuringRender(camera, () => floors.FindAll(renderer => renderer.forceRenderingOff).Count == 3 && wall.forceRenderingOff),
            "independent floor camera cap limits floor preparation while retaining the separate structural lease");
        Check(PerfMonitor.Counts["Terrain.FloorSubstitutes"] == 3 && PerfMonitor.Counts["Terrain.StructuralSubstitutes"] == 1,
            "floor cap completion retains original native fallback for every omitted floor");
        ScenarioTerrainBudget.Shutdown();
        ScenarioTerrainBudget.ConfigureFloorMeshReady(renderer => { });
        ScenarioTerrainBudget.ConfigureRoomFloorCameraSourceLimit(() => 0);
        ScenarioTerrainBudget.ConfigureRoomArchitecture(() => false, () => 100);
        ScenarioTerrainBudget.ConfigureArchitectureBank(mesh => 0);
        Object.DestroyImmediate(host); Object.DestroyImmediate(scenario); Object.DestroyImmediate(material);
        ProceduralWall.m_WallCache.Clear();
    }
}
