using System.IO;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class TerrainProgram
{
    private static void RetiredPillars(Camera camera)
    {
        Check(PillarCoverageData.Entries.Length == 10, "all ten retired pillar identities execute native-retention coverage");
        foreach (TerrainCoverageData.Entry data in PillarCoverageData.Entries)
        {
            ProceduralWall.m_WallCache.Clear();
            var host = new GameObject("GloomhavenVR.RetiredPillarProbe");
            var scenario = new GameObject("Scenario"); scenario.AddComponent<ProceduralScenario>();
            var root = new GameObject("AuditedStructuralBody"); root.transform.SetParent(scenario.transform, false);
            ProceduralWall.m_WallCache.Add(root.AddComponent<ProceduralWall>());
            var content = new GameObject("Generated Content"); content.transform.SetParent(root.transform, false);
            var obj = new GameObject(data.Name); obj.transform.SetParent(content.transform, false);
            Mesh exact = ScenarioEnvironmentMeshStream.Read(File.ReadAllBytes(data.Exact));
            Mesh original = Object.Instantiate(exact); original.name = data.Name; original.UploadMeshData(true);
            Bank.Add(original, new[] { exact, exact });
            float scale = 1.6f / Mathf.Max(exact.bounds.size.x, exact.bounds.size.y, exact.bounds.size.z);
            obj.transform.localScale = Vector3.one * scale;
            obj.transform.localPosition = Vector3.up - exact.bounds.center * scale;
            var filter = obj.AddComponent<MeshFilter>(); filter.sharedMesh = original;
            var renderer = obj.AddComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Amp_Basic_N_MRAO")); material.SetColor("_Tint", Color.white);
            var slots = new Material[original.subMeshCount];
            for (int slot = 0; slot < slots.Length; slot++) slots[slot] = material;
            renderer.sharedMaterials = slots;
            var collider = obj.AddComponent<MeshCollider>(); collider.sharedMesh = original;
            camera.transform.position = new Vector3(.8f, 1.8f, -3); camera.transform.LookAt(Vector3.up);
            PerfConfig.TerrainSubstitutionOn = true;
            PerfConfig.TerrainDetailPercent = 0; PerfConfig.DistantTerrainDetailPercent = 0;
            PerfConfig.CheapWallShadingOn = true;
            ScenarioTerrainBudget.ConfigureMeshBank(Bank.ContainsKey, Lookup);
            ScenarioTerrainBudget.Install(host); ScenarioTerrainBudget.QueueRoot(root); Morph(host);
            Check(Proxies(host).Count == 0, "retired pillar never prepares a private proxy: " + data.Name);
            Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(renderer)
                && !DuringRender(camera, () => renderer.forceRenderingOff),
                "zero detail and cheap-wall shading cannot acquire a retired pillar: " + data.Name);
            Check(filter.sharedMesh == original && collider.sharedMesh == original && renderer.sharedMaterial == material,
                "retired pillar retains native mesh material and gameplay collision: " + data.Name);
            renderer.enabled = false;
            Check(!DuringRender(camera, () => renderer.forceRenderingOff) && !renderer.enabled,
                "retired pillar respects native disabled visibility: " + data.Name);
            renderer.enabled = true; renderer.forceRenderingOff = true; Render(camera);
            Check(renderer.forceRenderingOff, "retired pillar preserves a foreign native renderer mask: " + data.Name);
            renderer.forceRenderingOff = false;
            ScenarioTerrainBudget.Shutdown(); Bank.Remove(original);
            Object.DestroyImmediate(host); Object.DestroyImmediate(scenario);
            Object.DestroyImmediate(original); Object.DestroyImmediate(exact); Object.DestroyImmediate(material);
        }
        ProceduralWall.m_WallCache.Clear();
    }
}
