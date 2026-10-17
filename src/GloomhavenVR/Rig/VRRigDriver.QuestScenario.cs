using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Rig;

internal sealed partial class VRRigDriver
{
    private static int _questScenarioRecords;
    private int _questScenarioSamples;
    private float _questScenarioDue;

    /// <summary>
    /// B677 fixes the menu and pointer on hardware, but the first Tutorial has no
    /// visible environment while actors, UI and native floor picking still work.
    /// A collider does not prove that Apparance produced a renderer. Two bounded
    /// Update snapshots distinguish generation, native visibility/camera admission
    /// and the actual uploaded mesh/material inputs. Neither readable CPU bounds nor
    /// shader.isSupported proves that the headset's final GPU output is correct.
    /// Never replace a lit procedural mesh with the small unlit primitive repair.
    /// </summary>
    private void TickQuestScenarioEvidence()
    {
        // No clock, native census, strings or arrays at normal logging/desktop.
        // The launch cap survives rig rebuilds and is shared by every rig instance.
        if (!QuestStandalonePlatform.Enabled || !VRLog.Wants(VRLogLevel.Debug)
            || _questScenarioRecords >= 8 || _questScenarioSamples >= 2
            || _camera == null)
            return;
        bool nativeScene = SceneManager.GetSceneByName("Game").isLoaded || SceneManager.GetSceneByName("Game_gamepad").isLoaded
            || SceneManager.GetSceneByName("ProcGen").isLoaded;
        if (_kind != RigKind.Scenario && !VRModeStateMachine.ScenarioBoardExists && !nativeScene) return;
        float now = Time.realtimeSinceStartup;
        if (_questScenarioDue == 0f) { _questScenarioDue = now + 3f; return; }
        if (now < _questScenarioDue) return;
        SceneController loading = SceneController.Instance;
        // Prefer settled content, but a stuck native loader must still leave
        // evidence. Never spend the launch's entire diagnosis waiting forever.
        if (loading != null && (loading.IsLoading || loading.ScenarioIsLoading)
            && now < _questScenarioDue + 30f) return;
        _questScenarioSamples++;
        _questScenarioDue = now + 15f;
        try { CaptureQuestScenarioEvidence(_camera, ResolveScenarioCamera(), nativeScene); }
        catch (Exception error) { QuestScenarioRecord("sample=" + _questScenarioSamples + " unavailable=" + error.GetType().Name); }
    }

    private void CaptureQuestScenarioEvidence(Camera head, Camera? native, bool nativeScene)
    {
        int sample = _questScenarioSamples;
        SceneController loading = SceneController.Instance;
        QuestScenarioRecord("sample=" + sample + " phase=update rig=" + _kind + " mode=" + VRModeStateMachine.CurrentMode
            + " board=" + VRModeStateMachine.ScenarioBoardExists + " nativeScene=" + nativeScene + " scene=" + Short(head.gameObject.scene.name)
            + " headMask=" + head.cullingMask.ToString("X8") + " anchorMask=" + (_anchor != null ? _anchor.cullingMask.ToString("X8") : "none")
            + " scenarioMask=" + (native != null ? native.cullingMask.ToString("X8") : "none")
            + " path=" + head.actualRenderingPath + " depth=" + head.depthTextureMode
            + " clips=" + Number(head.nearClipPlane) + "," + Number(head.farClipPlane)
            + " head=" + Vector(head.transform.position) + " loading=" + (loading != null && (loading.IsLoading || loading.ScenarioIsLoading))
            + " ambient=" + RenderSettings.ambientMode + "," + ColorText(RenderSettings.ambientLight)
            + " ambientIntensity=" + Number(RenderSettings.ambientIntensity) + " lights=" + UnityEngine.Object.FindObjectsOfType<Light>().Length
            + " lightmaps=" + LightmapSettings.lightmaps.Length + " wallFade=" + Shader.GetGlobalInt("ToggleWallFade")
            + " supportsInstancing=" + SystemInfo.supportsInstancing
            + " worldQuality=" + PerfConfig.WorldMaterialQualityMode + " terrain=" + PerfConfig.TerrainSubstitutionOn
            + " cheapWall=" + PerfConfig.CheapWallShadingOn + " detail=" + PerfConfig.TerrainDetailPercent
            + " static=" + PerfConfig.StaticScenarioBatchesOn + " instances=" + PerfConfig.EnvironmentDrawInstancingOn);

        ProceduralMapTile[] tiles = UnityEngine.Object.FindObjectsOfType<ProceduralMapTile>(true);
        var renderers = new List<Renderer>();
        var seen = new HashSet<int>();
        Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(head);
        int active = 0, all = 0, complete = 0, busy = 0, roots = 0;
        int componentEnabled = 0, hierarchyActive = 0, generatedMeshes = 0;
        int rootActive = 0, rootChildren = 0, populated = 0, handles = 0, frozen = 0, entitiesEnabled = 0, noProcedure = 0, parameterized = 0;
        var loaderStates = new int[8]; // no-loader, empty, never, loading, failed, unassigned, foreign-disabled, done
        int enabled = 0, masked = 0, culled = 0, inView = 0, meshes = 0, vertices = 0, unsupported = 0, missingColor = 0;
        Renderer? floor = null, other = null, fallback = null;
        int fallbackScore = -1;
        float floorArea = -1f;
        foreach (ProceduralMapTile tile in tiles)
        {
            if (tile == null) continue;
            if (tile.gameObject.activeInHierarchy) active++;
            if (tile.visibility == ProceduralMapTile.Visibility.All) all++;
            if (tile.IsPlacementCompleted) complete++;
            ApparanceEntity entity = tile.GetComponent<ApparanceEntity>();
            if (entity != null)
            {
                if (entity.IsBusy) busy++;
                if (entity.IsPopulated) populated++;
                if (entity.m_EntityHandle != 0) handles++;
                if (entity.Frozen) frozen++;
                if (entity.isActiveAndEnabled) entitiesEnabled++;
                if (entity.ProcedureID == 0) noProcedure++;
                // Object-typed Debug observation avoids adding a new production
                // Apparance.Net dependency solely to name ParameterCollection.
                // Both original Apparance assemblies retain their fields in the
                // Quest Player; a missing field reports no observed override.
                if (typeof(ApparanceEntity).GetField("PartialParameterOverride")?.GetValue(entity) != null) parameterized++;
            }
            Transform generated = tile.transform.Find("Generated Content");
            if (generated != null)
            {
                roots++;
                if (generated.gameObject.activeInHierarchy) rootActive++;
                rootChildren += generated.childCount;
            }
            renderers.Clear();
            tile.GetComponentsInChildren(true, renderers);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || seen.Count >= 8192 || !seen.Add(renderer.GetInstanceID())) continue;
                bool on = renderer.enabled && renderer.gameObject.activeInHierarchy;
                if (on) enabled++;
                if (renderer.enabled) componentEnabled++;
                if (renderer.gameObject.activeInHierarchy) hierarchyActive++;
                loaderStates[QuestScenarioLoaderIndex(MaterialLoaderHeal.DescribeForRenderer(renderer))]++;
                if (renderer.forceRenderingOff) masked++;
                bool layer = (head.cullingMask & (1 << renderer.gameObject.layer)) != 0;
                if (!layer) culled++;
                bool visible = on && layer && !renderer.forceRenderingOff && GeometryUtility.TestPlanesAABB(frustum, renderer.bounds);
                if (visible) inView++;
                Mesh? mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                meshes++; vertices += mesh.vertexCount;
                if (renderer.name.StartsWith("Generated Mesh (", StringComparison.Ordinal)) generatedMeshes++;
                // A black world may consist entirely of inactive authored previews.
                // The old census skipped their details and incorrectly made enabled=0
                // look like proof that Renderer.enabled itself was false. Observe the
                // actual closest candidate without changing any native visibility.
                int score = (renderer.gameObject.activeInHierarchy ? 4 : 0) + (renderer.enabled ? 2 : 0)
                    + (renderer.name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0);
                if (renderer is MeshRenderer && score > fallbackScore) { fallback = renderer; fallbackScore = score; }
                if (!mesh.HasVertexAttribute(VertexAttribute.Color)) missingColor++;
                Material material = renderer.sharedMaterial;
                if (material == null || material.shader == null || !material.shader.isSupported) unsupported++;
                if (renderer is not MeshRenderer || !on) continue;
                if (other == null) other = renderer;
                Bounds bounds = renderer.bounds;
                float area = Mathf.Abs(bounds.size.x * bounds.size.z);
                bool floorName = mesh.name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0
                    || renderer.name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0;
                if (floorName && (floor == null || area > floorArea)) { floor = renderer; floorArea = area; }
            }
        }
        ApparanceMeshInstances[] instancers = UnityEngine.Object.FindObjectsOfType<ApparanceMeshInstances>(true);
        int instanced = 0, instanceCount = 0;
        foreach (ApparanceMeshInstances source in instancers)
            if (source != null && source.isActiveAndEnabled) { instanced++; instanceCount += source.instanceCount; }
        ApparanceEngine engine = ApparanceEngine.Instance;
        QuestScenarioRecord("sample=" + sample + " tiles=" + tiles.Length + " active=" + active + " all=" + all + " complete=" + complete + " busy=" + busy
            + " generatedRoots=" + roots + " rootActive=" + rootActive + " rootChildren=" + rootChildren
            + " entityEnabled=" + entitiesEnabled + " populated=" + populated + " handles=" + handles + " frozen=" + frozen + " noProcedure=" + noProcedure
            + " parameterized=" + parameterized + " renderers=" + seen.Count + " scanCap=8192 enabled=" + enabled + " masked=" + masked + " layerExcluded=" + culled
            + " componentEnabled=" + componentEnabled + " hierarchyActive=" + hierarchyActive + " generatedMeshes=" + generatedMeshes
            + " ml=no/empty/never/loading/failed/unassigned/foreign/done:" + string.Join(",", loaderStates)
            + " inHeadFrustum=" + inView + " meshes=" + meshes + " vertices=" + vertices + " meshWithoutColor=" + missingColor + " unsupported=" + unsupported
            + " instancers=" + instancers.Length + " activeInstancers=" + instanced + " instances=" + instanceCount
            + " engine=" + (engine != null) + " engineActive=" + (engine != null && engine.isActiveAndEnabled) + " engineEntities=" + (engine != null ? engine.EntityComponents.Count : 0)
            + " engineInstancing=" + (engine != null && engine.EnableInstancedRendering));
        Renderer? representative = floor != null ? floor : other != null ? other : fallback;
        QuestScenarioRenderer(sample, floor != null ? "floor" : other != null ? "other" : "hidden-candidate", representative, head);
        QuestScenarioGpu(sample, representative);
    }

    private static int QuestScenarioLoaderIndex(string state)
    {
        if (state.StartsWith("empty-refs", StringComparison.Ordinal)) return 1;
        if (state.StartsWith("never-started", StringComparison.Ordinal)) return 2;
        if (state.StartsWith("loading ", StringComparison.Ordinal)) return 3;
        if (state.StartsWith("null-result", StringComparison.Ordinal)) return 4;
        if (state.StartsWith("done-stuck", StringComparison.Ordinal)) return 5;
        if (state.StartsWith("done-disabled", StringComparison.Ordinal)) return 6;
        if (state == "done") return 7;
        return 0;
    }

    private static string QuestScenarioInactiveRoot(Transform source)
    {
        for (Transform current = source; current != null; current = current.parent)
            if (!current.gameObject.activeSelf) return Short(current.name);
        return "none";
    }

    private static void QuestScenarioRenderer(int sample, string role, Renderer? renderer, Camera head)
    {
        if (renderer == null) { QuestScenarioRecord("sample=" + sample + " role=" + role + " renderer=none"); return; }
        Mesh? mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
        Material material = renderer.sharedMaterial;
        Texture? texture = material != null && material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
        string attributes = "";
        if (mesh != null)
            foreach (VertexAttribute attribute in new[] { VertexAttribute.Position, VertexAttribute.Normal, VertexAttribute.Tangent, VertexAttribute.Color, VertexAttribute.TexCoord0, VertexAttribute.TexCoord1 })
                if (mesh.HasVertexAttribute(attribute)) attributes += attribute + ":" + mesh.GetVertexAttributeFormat(attribute) + ":" + mesh.GetVertexAttributeDimension(attribute) + ",";
        QuestScenarioRecord("sample=" + sample + " role=" + role + " renderer=" + Short(renderer.name) + " layer=" + renderer.gameObject.layer
            + " enabled=" + renderer.enabled + " active=" + renderer.gameObject.activeInHierarchy + " masked=" + renderer.forceRenderingOff
            + " inactiveRoot=" + QuestScenarioInactiveRoot(renderer.transform)
            + " ml=" + Short(MaterialLoaderHeal.DescribeForRenderer(renderer))
            + " admitted=" + ((head.cullingMask & (1 << renderer.gameObject.layer)) != 0) + " bounds=" + Vector(renderer.bounds.center) + "/" + Vector(renderer.bounds.size)
            + " mesh=" + (mesh != null ? Short(mesh.name) + ",v=" + mesh.vertexCount + ",parts=" + mesh.subMeshCount + ",indices=" + (mesh.subMeshCount > 0 ? mesh.GetIndexCount(0) : 0) + ",read=" + mesh.isReadable + ",stride=" + mesh.GetVertexBufferStride(0) : "none")
            + " attrs=" + attributes + " shader=" + (material != null && material.shader != null ? Short(material.shader.name) : "none")
            + " supported=" + (material != null && material.shader != null && material.shader.isSupported) + " instancing=" + (material != null && material.enableInstancing)
            + " mpb=" + renderer.HasPropertyBlock() + " texture=" + (texture != null ? Short(texture.name) + "," + texture.width + "x" + texture.height : "none")
            + " tint=" + (material != null && material.HasProperty("_Tint") ? ColorText(material.GetColor("_Tint")) : "none")
            + " color=" + (material != null && material.HasProperty("_Color") ? ColorText(material.GetColor("_Color")) : "none"));
    }

    private static void QuestScenarioRecord(string text)
    {
        if (_questScenarioRecords >= 8) return;
        _questScenarioRecords++;
        // One complete Android record, including timestamp/prefix. Names can be
        // supplied by game assets; strip controls and cap the formatted payload.
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        while (Encoding.UTF8.GetByteCount(text) > 880) text = text.Substring(0, text.Length - 1);
        UnityEngine.Debug.Log("[Quest startup] scenario " + text);
    }
    private static string Short(string text) => text.Length <= 48 ? text : text.Substring(0, 48);
    private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Vector(Vector3 value) => Number(value.x) + "," + Number(value.y) + "," + Number(value.z);
    private static string ColorText(Color value) => Number(value.r) + "," + Number(value.g) + "," + Number(value.b) + "," + Number(value.a);
}
