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
            int role = data.Role == 1 ? 1 : TerrainWorldBlockProof.CertifiedFloorSupport(data.Name) ? 3 : 2;
            ScenarioTerrainBudget.ConfigureArchitectureBank(mesh => mesh == original ? role : 0);
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
            if (data.Name.StartsWith("CR_ST_FloorShelf_", StringComparison.Ordinal))
            {
                material.shader = Shader.Find("Amp_Basic_N_MRAO"); material.SetFloat("_WallFade_On", 1f);
                PerfConfig.CheapWallShadingOn = true; Morph(host);
                var map = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true); map.filterMode = FilterMode.Bilinear;
                var values = new Color[64]; for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) values[y * 8 + x] = new Color(.06f + .94f * x / 7f, 0, 0, 0);
                map.SetPixels(values); map.Apply();
                var block = new MaterialPropertyBlock(); block.SetTexture("_TilesOcclusionMap", map); block.SetInteger("ToggleWallFade", 1);
                block.SetFloat("_EnableOcclusionMap", 1);
                int firstPixels = 0, previous = int.MaxValue, changes = 0;
                for (int step = 0; step <= 10; step++)
                {
                    block.SetFloat("_Cutoff", -.15f + step * .13f); renderer.SetPropertyBlock(block);
                    Check(DuringRender(camera, () =>
                    {
                        var proxy = Proxies(host)[0]; var current = new MaterialPropertyBlock(); proxy.GetPropertyBlock(current);
                        return renderer.forceRenderingOff && current.GetFloat("_GHVRTerrainNeverFade") == 0f && current.GetFloat("_GHVRWorldNeverFade") == 0f;
                    }), "native FloorShelf furniture preserves animated wall-fade markers despite its Floor spelling: " + data.Name);
                    int visible = Visible(Pixels(camera)); if (step == 0) firstPixels = visible;
                    Check(visible <= previous, "native FloorShelf fade has monotone actual camera coverage: " + data.Name);
                    if (visible != previous) changes++; previous = visible;
                }
                UnityEngine.Debug.Log("Native shelf fade " + data.Name + ": first=" + firstPixels + ", final=" + previous + ", changes=" + changes);
                Check(firstPixels > 10 && previous == 0 && changes >= 2,
                    "native FloorShelf source geometry retains visible intermediate fade pixels and completely disappears at its endpoint: " + data.Name
                    + "; first=" + firstPixels + ", final=" + previous + ", changes=" + changes);
                renderer.SetPropertyBlock(null); Object.DestroyImmediate(map);
            }
            else if (role == 3)
            {
                Check(!ScenarioTerrainBudget.CanGroupRoomFloor(original) && !ScenarioTerrainBudget.TryGetSettledRoomFloor(renderer, out _),
                    "native certified floor support stays outside core-floor grouping: " + data.Name);
                PerfConfig.CheapWallShadingOn = true; Morph(host);
                Check(DuringRender(camera, () =>
                {
                    var current = new MaterialPropertyBlock(); Proxies(host)[0].GetPropertyBlock(current);
                    return renderer.forceRenderingOff && current.GetFloat("_GHVRTerrainNeverFade") == 1f && current.GetFloat("_GHVRWorldNeverFade") == 1f;
                }), "native certified floor support preserves genuine floor fade safety: " + data.Name);
            }
            ScenarioTerrainBudget.Shutdown(); ScenarioTerrainBudget.ConfigureRoomArchitecture(() => false, () => 100);
            Bank.Remove(original); Object.DestroyImmediate(host); Object.DestroyImmediate(scenario); Object.DestroyImmediate(material);
            Object.DestroyImmediate(original); Object.DestroyImmediate(exact); Object.DestroyImmediate(coarse);
        }
        PerfConfig.TerrainSubstitutionOn = true;
    }
}
