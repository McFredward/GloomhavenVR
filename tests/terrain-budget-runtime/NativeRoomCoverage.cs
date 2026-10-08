using System;
using System.IO;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class TerrainProgram
{
    private static void NativeRoomCoverage(Camera camera)
    {
        foreach (TerrainRoomCoverageData.Entry data in TerrainRoomCoverageData.Entries)
        {
            var host = new GameObject("GloomhavenVR.NativeRoomCoverage");
            var scenario = new GameObject("Scenario"); scenario.AddComponent<ProceduralScenario>();
            var tile = new GameObject("NativeRoomTile"); tile.transform.SetParent(scenario.transform, false); tile.AddComponent<ProceduralMapTile>();
            var content = new GameObject("Generated Content"); content.transform.SetParent(tile.transform, false);
            var obj = new GameObject(data.Name); obj.transform.SetParent(content.transform, false);
            Mesh exact = ScenarioEnvironmentMeshStream.Read(File.ReadAllBytes(data.Exact));
            Mesh coarse = ScenarioEnvironmentMeshStream.Read(File.ReadAllBytes(data.Coarse));
            Mesh original = Object.Instantiate(exact); original.name = data.Name; original.UploadMeshData(true);
            Bank.Add(original, new[] { exact, coarse });
            var filter = obj.AddComponent<MeshFilter>(); filter.sharedMesh = original;
            var renderer = obj.AddComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Amp_Basic_N_MRAO")); material.SetColor("_Tint", Color.white);
            var slots = new Material[original.subMeshCount]; for (int index = 0; index < slots.Length; index++) slots[index] = material;
            renderer.sharedMaterials = slots;
            var collider = obj.AddComponent<MeshCollider>(); collider.sharedMesh = original;
            float scale = 1.5f / Mathf.Max(exact.bounds.size.x, exact.bounds.size.y, exact.bounds.size.z);
            obj.transform.localScale = Vector3.one * scale; obj.transform.localPosition = Vector3.up - exact.bounds.center * scale;
            PerfConfig.CheapWallShadingOn = false; PerfConfig.TerrainSubstitutionOn = false;
            PerfConfig.TerrainDetailPercent = PerfConfig.DistantTerrainDetailPercent = 0;
            ScenarioTerrainBudget.ConfigureRoomArchitecture(() => true, () => 0);
            ScenarioTerrainBudget.ConfigureRoomFloorPreparation(() => false);
            ScenarioTerrainBudget.ConfigureArchitectureBank(mesh => mesh == original ? data.Role : 0);
            ScenarioTerrainBudget.ConfigureMeshBank(Bank.ContainsKey, Lookup);
            ScenarioTerrainBudget.ConfigureAssetPreparation(() => true, () => false);
            ScenarioTerrainBudget.ConfigureFloorGrouping(current => false);
            ScenarioTerrainBudget.ConfigureWorldMaterialIntegration(current => current, () => new MaterialPass(), () => false, current => false);
            ScenarioTerrainBudget.Install(host); ScenarioTerrainBudget.QueueRoot(scenario); Morph(host, 48);
            foreach (Vector3 view in new[] { new Vector3(0, 3, -3), new Vector3(2, 3, 2) })
            {
                camera.transform.position = view; camera.transform.LookAt(Vector3.up); camera.orthographicSize = 1.5f;
                Color[] pixels = Pixels(camera);
                Check(Visible(pixels) > 10, "source-bound room geometry has nonempty camera pixels from both original 3D views: " + data.Name);
            }
            if (Triangles(coarse) < Triangles(exact))
            {
                Check(DuringRender(camera, () => renderer.forceRenderingOff), "source-bound room coarse endpoint acquires an actual native camera lease: " + data.Name);
                Check(PerfMonitor.Counts["Terrain.SubmittedTriangles"] == Triangles(coarse)
                    && PerfMonitor.Counts["Terrain.OriginalTriangles"] == data.Triangles,
                    "source-bound room endpoint counts actual reduced native/DLC camera triangles: " + data.Name);
            }
            Check(filter.sharedMesh == original && collider.sharedMesh == original && renderer.sharedMaterials.Length == original.subMeshCount
                && original.bounds == exact.bounds && coarse.bounds == exact.bounds,
                "source-bound floor architecture retains native bounds collisions and all original submesh material slots: " + data.Name);
            ScenarioTerrainBudget.Shutdown(); ScenarioTerrainBudget.ConfigureRoomArchitecture(() => false, () => 100);
            Bank.Remove(original); Object.DestroyImmediate(host); Object.DestroyImmediate(scenario); Object.DestroyImmediate(material);
            Object.DestroyImmediate(original); Object.DestroyImmediate(exact); Object.DestroyImmediate(coarse);
        }
        PerfConfig.TerrainSubstitutionOn = true;
    }
}
