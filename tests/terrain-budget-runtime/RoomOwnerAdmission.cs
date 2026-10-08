using System;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class TerrainProgram
{
    private static void RoomOwnerAdmission(Camera camera)
    {
        var host = new GameObject("GloomhavenVR.RoomOwnerAdmission");
        var scenario = new GameObject("Scenario"); scenario.AddComponent<ProceduralScenario>();
        var native = new Material(Shader.Find("Amp_Low/Amp_Basic_N_MRAO_Low")); native.SetColor("_Tint", Color.white);
        native.SetFloat("_WallFade_On", 1f); // Serialized native LOW flag, no unsupported program keyword.
        var floor = Surface(scenario, "CertifiedNativeLowFloor", Vector3.up, native, false);
        Mesh original = floor.GetComponent<MeshFilter>().sharedMesh;
        int percent = 100; bool world = false, prepare = true;
        var variant = new Material(native) { shader = Shader.Find("GloomhavenVR/ScenarioCheapTerrain") };
        variant.SetFloat("_GHVRTerrainNativeRoute", 0f);
        TerrainWorldBlockProof.Canonical = value => value == variant ? native : value;
        ScenarioTerrainBudget.ConfigureBlockEffectAdmission(TerrainWorldBlockProof.HasUnsupportedBlock);
        ScenarioTerrainBudget.ConfigureRoomArchitecture(() => true, () => percent);
        ScenarioTerrainBudget.ConfigureRoomFloorPreparation(() => prepare);
        ScenarioTerrainBudget.ConfigureArchitectureBank(mesh => mesh == original ? 1 : 0);
        ScenarioTerrainBudget.ConfigureMeshBank(Bank.ContainsKey, Lookup);
        ScenarioTerrainBudget.ConfigureAssetPreparation(() => true, () => false);
        ScenarioTerrainBudget.ConfigureCanonicalMaterial(value => value);
        ScenarioTerrainBudget.ConfigureFloorGrouping(value => false);
        ScenarioTerrainBudget.ConfigureWorldMaterialIntegration(value => TerrainWorldBlockProof.ProgramAllowed(value) ? variant : value,
            () => new MaterialPass(), () => world, value => value == variant);
        PerfConfig.TerrainSubstitutionOn = false; PerfConfig.CheapWallShadingOn = false;
        PerfConfig.TerrainDetailPercent = PerfConfig.DistantTerrainDetailPercent = 100;
        PerfConfig.SharedEnvironmentMaterialReadsOn = true;
        ScenarioTerrainBudget.Install(host); ScenarioTerrainBudget.QueueRoot(scenario); Morph(host);
        Check(ScenarioTerrainBudget.TryGetSettledRoomFloor(floor, out Mesh exact) && Triangles(exact) == Triangles(original),
            "full-quality floor grouping independently prepares exact geometry with legacy terrain cheap shading and world mode all off");
        Check(!DuringRender(camera, () => floor.forceRenderingOff), "exact floor preparation does not add a redundant terrain draw");
        percent = 0; prepare = false; Morph(host); world = true;
        Check(DuringRender(camera, () => floor.forceRenderingOff),
            "audited world owner admits coarse native LOW floor despite its serialized wall-fade flag");
        native.EnableKeyword("UNKNOWN_NATIVE_FX");
        Check(!DuringRender(camera, () => floor.forceRenderingOff), "unknown native LOW program keyword retains original floor geometry");
        native.DisableKeyword("UNKNOWN_NATIVE_FX");
        foreach (string key in new[] { "_UseTextureEmission", "_Fresnel_On", "_AdvancedEmission", "_MossTexture_ON", "_ToggleDissolve", "_Cutout_VertexPos_Influence" })
        {
            var block = new MaterialPropertyBlock(); block.SetFloat(key, 1f);
            floor.SetPropertyBlock(block);
            Check(!DuringRender(camera, () => floor.forceRenderingOff), "world-owned floor wide MPB effect retains native geometry: " + key);
            floor.SetPropertyBlock(null); floor.SetPropertyBlock(block, 0);
            Check(!DuringRender(camera, () => floor.forceRenderingOff), "world-owned floor slot MPB effect retains native geometry: " + key);
            floor.SetPropertyBlock(null, 0);
        }
        var safe = new MaterialPropertyBlock(); safe.SetColor("_Tint", Color.cyan); floor.SetPropertyBlock(safe, 0);
        Check(DuringRender(camera, () => floor.forceRenderingOff), "safe native LOW floor slot tint retains coarse audited world geometry");
        floor.SetPropertyBlock(null, 0);
        world = false;
        Check(!DuringRender(camera, () => floor.forceRenderingOff), "legacy cheap route still refuses native LOW saved wall-fade flag without audited world owner");
        ScenarioTerrainBudget.Shutdown(); ScenarioTerrainBudget.ConfigureRoomArchitecture(() => false, () => 100);
        ScenarioTerrainBudget.ConfigureRoomFloorPreparation(() => false);
        ScenarioTerrainBudget.ConfigureWorldMaterialIntegration(value => value, () => new MaterialPass(), () => false, value => false);
        TerrainWorldBlockProof.Canonical = value => value;
        Object.DestroyImmediate(host); Object.DestroyImmediate(scenario); Object.DestroyImmediate(native); Object.DestroyImmediate(variant);
        PerfConfig.TerrainSubstitutionOn = true;
    }
}
