using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class TerrainProgram
{
    private static bool SameCoverage(Color[] left, Color[] right)
    {
        for (int pixel = 0; pixel < left.Length; pixel++)
            if ((left[pixel].r + left[pixel].g + left[pixel].b > .05f)
                != (right[pixel].r + right[pixel].g + right[pixel].b > .05f)) return false;
        return true;
    }
    private static void NativeCoverage(Camera camera)
    {
        ProceduralWall.m_WallCache.Clear();
        Check(TerrainCoverageData.Entries.Length == 10, "all ten captured structural definitions execute source-bound coverage");
        foreach (TerrainCoverageData.Entry data in TerrainCoverageData.Entries)
        {
            var host = new GameObject("GloomhavenVR.SourceBoundTerrain");
            var scenario = new GameObject("Scenario"); scenario.AddComponent<ProceduralScenario>();
            var root = new GameObject("TO_INT_Audited_Wall_Body_PR"); root.transform.SetParent(scenario.transform, false);
            ProceduralWall.m_WallCache.Add(root.AddComponent<ProceduralWall>());
            var content = new GameObject("Generated Content"); content.transform.SetParent(root.transform, false);
            var obj = new GameObject(data.Name); obj.transform.SetParent(content.transform, false);
            Mesh exact = ScenarioEnvironmentMeshStream.Read(File.ReadAllBytes(data.Exact));
            Mesh coarse = ScenarioEnvironmentMeshStream.Read(File.ReadAllBytes(data.Coarse));
            Mesh original = Object.Instantiate(exact); original.name = data.Name; original.UploadMeshData(true);
            Bank.Add(original, new[] { exact, coarse });
            float scale = 1.6f / Mathf.Max(exact.bounds.size.x, exact.bounds.size.y, exact.bounds.size.z);
            obj.transform.localScale = Vector3.one * scale;
            obj.transform.localPosition = Vector3.up - exact.bounds.center * scale;
            var filter = obj.AddComponent<MeshFilter>(); filter.sharedMesh = original;
            var renderer = obj.AddComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Amp_Basic_N_MRAO")); material.SetColor("_Tint", Color.white);
            var slots = new Material[original.subMeshCount]; for (int slot = 0; slot < slots.Length; slot++) slots[slot] = material;
            renderer.sharedMaterials = slots;
            var collision = obj.AddComponent<MeshCollider>(); collision.sharedMesh = original;
            camera.transform.position = new Vector3(.8f, 1.8f, -3); camera.transform.LookAt(Vector3.up);
            camera.orthographicSize = 1.5f;
            PerfConfig.CheapWallShadingOn = false; PerfConfig.TerrainDetailPercent = 100; PerfConfig.DistantTerrainDetailPercent = 100;
            ScenarioTerrainBudget.ConfigureAssetPreparation(() => true, () => false);
            ScenarioTerrainBudget.ConfigureMeshBank(Bank.ContainsKey, Lookup);
            ScenarioTerrainBudget.ConfigureCanonicalMaterial(current => current);
            ScenarioTerrainBudget.ConfigureNativeCameraConsumers(current => current.commandBufferCount > 0);
            ScenarioTerrainBudget.Install(host); ScenarioTerrainBudget.QueueRoot(scenario); Tick(host);
            Check(Proxies(host).Count == 0, "original quality keeps source-bound structural renderer native");
            Color[] nativePixels = Pixels(camera);
            Check(Visible(nativePixels) > 10, "original native structural geometry has nonempty source-bound camera pixels: " + data.Name);
            PerfConfig.CheapWallShadingOn = true; Tick(host);
            Check(Proxies(host).Count == 1 && DuringRender(camera, () => renderer.forceRenderingOff),
                "captured structural definition is prepared and leased with immutable bank provenance: " + data.Name);
            Color[] cheapPixels = Pixels(camera);
            Check(SameCoverage(nativePixels, cheapPixels), "source-bound cheap structural surface preserves original silhouette coverage: " + data.Name);
            Check(PerfMonitor.Counts["Terrain.CheapSurfaces"] == 1 && PerfMonitor.Counts["Terrain.OriginalTriangles"] == data.Triangles,
                "completed terrain counters measure one actual surviving source-bound camera lease");
            PerfConfig.CheapWallShadingOn = false; PerfConfig.TerrainDetailPercent = 0; Tick(host, 5f); Pixels(camera);
            Mesh first = Proxies(host)[0].GetComponent<MeshFilter>().sharedMesh;
            Check(Triangles(first) == data.Triangles && first != coarse,
                "actual native structural transition keeps original topology in its intermediate 3D shape");
            Morph(host); Color[] reducedPixels = Pixels(camera);
            Mesh endpoint = Proxies(host)[0].GetComponent<MeshFilter>().sharedMesh;
            Check(Triangles(endpoint) == Triangles(coarse) && Triangles(endpoint) < data.Triangles,
                "source-bound coarse 3D endpoint reduces actual native structural triangles: " + data.Name);
            Check(endpoint.bounds.size.x > 0 && endpoint.bounds.size.y > 0 && endpoint.bounds.size.z > 0 && Visible(reducedPixels) > 10,
                "source-bound strongest structural representation retains nonempty actual 3D volume and camera pixels");
            Check(filter.sharedMesh == original && !original.isReadable && collision.sharedMesh == original && renderer.sharedMaterials.Length == slots.Length,
                "source-bound native mesh collision material slots and unreadable original stay untouched");
            PerfConfig.CheapWallShadingOn = true; PerfConfig.TerrainDetailPercent = 100; Morph(host);
            var block = new MaterialPropertyBlock(); block.SetFloat("_UseEmissiveMap", 1f); renderer.SetPropertyBlock(block, slots.Length - 1);
            Check(!DuringRender(camera, () => renderer.forceRenderingOff), "actual source-bound structural material-slot native effect veto remains immediate");
            renderer.SetPropertyBlock(null, slots.Length - 1);
            foreach (string boundary in new[] { "PCG_TO_INT_UnderWall_01_PR", "TO_INT_Stone_Doorway_01_FRAME_Split_PR", "TO_INT_Entrance_Thin_PR", "TO_INT_EXIT_Thick_PR", "AuditedTopCap" })
            {
                root.name = boundary;
                Check(!DuringRender(camera, () => renderer.forceRenderingOff),
                    "same audited structural mesh under a foundation cap or doorway template stays native: " + boundary);
            }
            root.name = "TO_INT_Audited_Wall_Body_PR";
            bool leased = false;
            Camera.CameraCallback revoke = current =>
            {
                if (current != camera) return;
                leased = renderer.forceRenderingOff; ScenarioTerrainBudget.BeforeNativeRendererWrite(renderer);
            };
            Camera.onPreCull += revoke; Render(camera); Camera.onPreCull -= revoke;
            Check(leased && PerfMonitor.Counts["Terrain.OriginalTriangles"] == 0 && PerfMonitor.Counts["Terrain.CheapSurfaces"] == 0,
                "source-bound late native write revokes camera coverage and completion counts together");
            GameObject? clone = null;
            Camera.CameraCallback cloneDuringCamera = current =>
            {
                if (current != camera) return;
                ScenarioTerrainBudget.BeforeNativeContentChange(); clone = Object.Instantiate(obj);
            };
            Camera.onPreCull += cloneDuringCamera; Render(camera); Camera.onPreCull -= cloneDuringCamera;
            Check(clone != null && clone.GetComponent<MeshFilter>().sharedMesh == original && !clone.GetComponent<MeshRenderer>().forceRenderingOff
                && clone.GetComponent<MeshRenderer>().sharedMaterials.Length == slots.Length && clone.transform.childCount == 0,
                "actual source-bound native clone during camera retains original mesh slots and unmasked presentation");
            renderer.enabled = false; PerfConfig.CheapWallShadingOn = false; Morph(host);
            Check(SameCoverage(nativePixels, Pixels(camera)), "source-bound cloned native structural original retains original camera silhouette pixels");
            Object.DestroyImmediate(clone); renderer.enabled = true;
            ScenarioTerrainBudget.Shutdown(); Object.DestroyImmediate(host); Object.DestroyImmediate(scenario);
            ProceduralWall.m_WallCache.Clear(); Bank.Remove(original);
            Object.DestroyImmediate(original); Object.DestroyImmediate(exact); Object.DestroyImmediate(coarse); Object.DestroyImmediate(material);
        }
    }
}
