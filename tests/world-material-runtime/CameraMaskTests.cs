using System;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static partial class WorldMaterialProgram
{
    private static void CameraMasks(GameObject host, GameObject room, MeshRenderer source,
        Material first, Material second, Camera world)
    {
        int oldLayer = source.gameObject.layer;
        Color oldTint = first.GetColor("_Tint");
        var uiRoot = new GameObject("Actual isolated UI content"); uiRoot.AddComponent<Canvas>();
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.transform.SetParent(uiRoot.transform, false);
        quad.layer = 31; quad.transform.position = new Vector3(0, 0, -.6f);
        var uiMaterial = new Material(Shader.Find("Unlit/Color")); uiMaterial.color = Color.green;
        quad.GetComponent<MeshRenderer>().sharedMaterial = uiMaterial;
        var cameraGo = new GameObject("UI capture"); var ui = cameraGo.AddComponent<Camera>();
        ui.CopyFrom(world); ui.enabled = false; ui.cullingMask = 1 << 31;
        ui.stereoTargetEye = StereoTargetEyeMask.None;
        ui.targetTexture = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBFloat); ui.targetTexture.Create();
        MeshRenderer? proxy = null;
        int revocations = 0, eligibilityReads = 0, consumerReads = 0;
        void ResetReads()
        {
            NativeWriteObserver.MeshReads = NativeWriteObserver.MaterialReads = NativeWriteObserver.PropCopies = 0;
            NativeWriteObserver.RegistryVisits = NativeWriteObserver.RendererBlockReads = NativeWriteObserver.SlotBlockReads = 0;
            NativeWriteObserver.ArrayWrites = NativeWriteObserver.MaterialCopies = 0;
            PerfMonitor.ScopeCalls.Clear(); PerfMonitor.Counts.Clear();
        }
        void Release(Renderer renderer)
        {
            if (renderer != source || proxy == null || !proxy.enabled) return;
            revocations++; proxy.enabled = false; source.forceRenderingOff = false;
        }
        void SaveImage(Camera camera, string name)
        {
            var image = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            RenderTexture old = RenderTexture.active; RenderTexture.active = camera.targetTexture;
            try
            {
                image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(Environment.GetEnvironmentVariable("GHVR_WORLD_EVIDENCE")!,
                    typeof(WorldMaterialProgram).Assembly.GetName().Name + "-ui-" + name + ".png"), image.EncodeToPNG());
            }
            finally { RenderTexture.active = old; Object.DestroyImmediate(image); }
        }
        void PrepareProxy(bool exactReader)
        {
            first.SetFloat("_AddVertexAnim", 0f); source.enabled = true; source.forceRenderingOff = false;
            source.sharedMaterials = new[] { first, second }; Center(world);
            proxy = Source("Current geometry consumer on a different camera layer", uiRoot.transform,
                source.GetComponent<MeshFilter>().sharedMesh, WorldMaterialBudget.VariantFor(first));
            proxy.gameObject.layer = 31; source.forceRenderingOff = true;
            WorldMaterialBudget.ConfigureRenderSubstituteOwnership(renderer =>
            { eligibilityReads++; return renderer == source && proxy != null && proxy.enabled; });
            WorldMaterialBudget.ConfigureRenderSubstituteRevocation(exactReader ? renderer =>
            { consumerReads++; return renderer == source && proxy != null && proxy.enabled; } : null!);
            WorldMaterialBudget.ConfigureSourceChanged(Release);
            first.SetFloat("_AddVertexAnim", 1f);
        }
        try
        {
            source.gameObject.layer = 0; source.enabled = true; source.forceRenderingOff = false;
            source.SetPropertyBlock(null); source.sharedMaterials = new[] { first, second };
            first.SetColor("_Tint", Color.red); PerfConfig.SharedEnvironmentMaterialReadsOn = true;
            Center(world); Material retained = source.sharedMaterial;
            Check(WorldMaterialBudget.IsOwnedVariant(retained), "UI mask fixture starts with a current native world variant");
            ResetReads(); Color firstUI = Center(ui); Color secondUI = Center(ui);
            Check(firstUI.g > .8f && firstUI.r < .1f && secondUI.g > .8f,
                "actual isolated UI cameras render their original content while world sources stay unreachable");
            Check(PerfMonitor.ScopeCalls.TryGetValue("WorldMaterial.PreCull", out int uiCallbacks) && uiCallbacks == 2,
                "every UI capture retains its actual current native final callback and read-pass lifecycle");
            int excluded = PerfMonitor.Counts["WorldMaterial.CameraExcludedCandidates"];
            string evidence = Environment.GetEnvironmentVariable("GHVR_WORLD_EVIDENCE")!;
            SaveImage(ui, "isolated");
            // Record both production and the source-controlled old full-work reference
            // before its intentional assertion failure; there is no player-facing switch.
            File.WriteAllText(Path.Combine(evidence, typeof(WorldMaterialProgram).Assembly.GetName().Name + "-ui-camera-counts.txt"),
                "Actual UI Camera.Render callbacks=" + uiCallbacks + "; mesh reads=" + NativeWriteObserver.MeshReads
                + "; material reads=" + NativeWriteObserver.MaterialReads + "; scope nodes/last callback="
                + PerfMonitor.Counts["WorldMaterial.ScopeNodeReads"] + "; prop traversals=" + NativeWriteObserver.PropCopies
                + "; excluded candidates/last callback=" + excluded + ".\n"
                + "Unconditional production guard compared with the guard-omitted source mutation; no player-facing comparison option.\n"
                + "Counts are actual source-extracted Unity operations, not headset FPS or timing.\n");
            Check(NativeWriteObserver.MeshReads == 0 && NativeWriteObserver.MaterialReads == 0
                && NativeWriteObserver.PropCopies == 0 && NativeWriteObserver.RegistryVisits == 0
                && NativeWriteObserver.ArrayWrites == 0 && NativeWriteObserver.MaterialCopies == 0
                && PerfMonitor.Counts["WorldMaterial.ScopeNodeReads"] == 0 && source.sharedMaterial == retained,
                "two unreachable UI captures perform zero world material mesh scope or prop traversal");
            Check(PerfMonitor.Counts["WorldMaterial.CameraExcludedCandidates"] > 0,
                "bounded Debug camera-excluded count reports actual skipped world candidates");
            ui.cullingMask |= 1; first.SetFloat("_AddVertexAnim", 1f); ResetReads(); Center(ui);
            Check(source.sharedMaterial == first && NativeWriteObserver.MeshReads > 0,
                "current native camera mask change immediately validates newly reachable world sources");
            ui.cullingMask = 1 << 31; first.SetFloat("_AddVertexAnim", 0f); Center(world);
            source.gameObject.layer = 31; first.SetFloat("_AddVertexAnim", 1f); ResetReads(); Center(ui);
            Check(source.sharedMaterial == first && NativeWriteObserver.MeshReads > 0,
                "late native source re-layer into current UI mask immediately retains its native effect");
            first.SetFloat("_AddVertexAnim", 0f); Center(ui); source.transform.SetParent(uiRoot.transform, false);
            Center(ui);
            Check(source.sharedMaterial == first,
                "previously adopted world source moved into Canvas restores original presentation in the same UI camera");
            source.transform.SetParent(room.transform, false); source.gameObject.layer = 0; Center(world);
            Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),
                "next world eye refreshes native material ownership after current UI scope changes");
            first.SetFloat("_AddVertexAnim", 1f); ResetReads();
            object driver = typeof(WorldMaterialBudget).GetField("_driver", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            driver.GetType().GetMethod("PreCull", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(driver, new object?[] { null });
            Check(source.sharedMaterial == first && NativeWriteObserver.MeshReads > 0,
                "null camera retains the complete original native world validation path");
            first.SetFloat("_AddVertexAnim", 0f); Center(world);

            // CommandBuffer.DrawRenderer ignores the camera layer mask. Install it in
            // a real late callback, so the actual final Harmony boundary must see it.
            quad.SetActive(false); var commands = new CommandBuffer { name = "Native DrawRenderer beyond UI mask" };
            commands.DrawRenderer(source, first, 0, 0);
            Camera.CameraCallback late = camera =>
            { if (camera == ui) { first.SetFloat("_AddVertexAnim", 1f); ui.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commands); } };
            Camera.onPreCull += late;
            try
            {
                ResetReads(); Color drawn = Center(ui);
                Check(source.sharedMaterial == first && NativeWriteObserver.MeshReads > 0 && drawn.r > .7f,
                    "late native DrawRenderer beyond UI mask retains complete current world effect validation and pixels");
                SaveImage(ui, "native-command-buffer");
            }
            finally { Camera.onPreCull -= late; ui.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commands); commands.Dispose(); }
            first.SetFloat("_AddVertexAnim", 0f); Center(world); quad.SetActive(true);

            // An exact current/queued consumer can draw a proxy on a different layer.
            PrepareProxy(true); eligibilityReads = consumerReads = 0; Center(ui);
            Check(proxy != null && !proxy.enabled && !source.forceRenderingOff && source.sharedMaterial == first && revocations == 1,
                "unreachable original with current differently layered consumer retains same-camera native revocation");
            Check(eligibilityReads <= 1 && consumerReads == 1,
                "excluded-source consumer fallback reuses exact current revocation read within the complete path");
            Object.DestroyImmediate(proxy!.gameObject); proxy = null;
            PrepareProxy(false); Center(ui);
            Check(proxy != null && !proxy.enabled && revocations == 2,
                "unwired exact reader conservatively preserves current legacy ownership fallback");
            Object.DestroyImmediate(proxy!.gameObject); proxy = null;
            first.SetFloat("_AddVertexAnim", 0f); source.forceRenderingOff = false;
            WorldMaterialBudget.ConfigureRenderSubstituteOwnership(_ => { eligibilityReads++; return true; });
            WorldMaterialBudget.ConfigureRenderSubstituteRevocation(_ => false); Center(world);
            eligibilityReads = 0; ResetReads(); Center(ui);
            Check(eligibilityReads == 0 && NativeWriteObserver.MaterialReads == 0,
                "prepared membership without a current consumer does not defeat exact unreachable-source skip");

            float ambient = .4f; WorldMaterialBudget.ConfigureAmbientWeight(() => ambient);
            using (WorldMaterialBudget.BeginMaterialReadPass())
            {
                Material before = WorldMaterialBudget.VariantFor(first);
                first.SetColor("_Tint", Color.blue); ambient = .85f; Center(ui);
                Material after = WorldMaterialBudget.VariantFor(first);
                Check(after == before && after.GetColor("_Tint") == Color.blue
                    && Mathf.Approximately(after.GetFloat("_GHVRWorldAmbientWeight"), .85f),
                    "nested zero-match UI camera invalidates current prepared material metadata and ambient");
                first.EnableKeyword("_ENABLE_ANIM"); Center(ui);
                Check(WorldMaterialBudget.VariantFor(first) == first,
                    "nested zero-match UI camera retains fresh native shader refusal for later factory reads");
                first.DisableKeyword("_ENABLE_ANIM");
            }
            // Config Off still runs settings before any layer exclusion and disposes
            // consumer references through the existing synchronous lifecycle path.
            PerfConfig.WorldMaterialQualityMode = 0; Center(ui);
            Check(source.sharedMaterial == first, "world option Off restores owned source materials even in a pure UI capture");
            PerfConfig.WorldMaterialQualityMode = 2; Tick(host, 16); Center(world);
            Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial), "first resumed world eye regains current native material ownership");
            SaveImage(world, "resumed-world");
        }
        finally
        {
            if (proxy != null) Object.DestroyImmediate(proxy.gameObject);
            source.transform.SetParent(room.transform, false); source.gameObject.layer = oldLayer;
            source.forceRenderingOff = false; first.SetFloat("_AddVertexAnim", 0f); first.DisableKeyword("_ENABLE_ANIM");
            first.SetColor("_Tint", oldTint); PerfConfig.SharedEnvironmentMaterialReadsOn = true;
            WorldMaterialBudget.ConfigureRenderSubstituteOwnership(_ => false);
            WorldMaterialBudget.ConfigureRenderSubstituteRevocation(_ => false);
            WorldMaterialBudget.ConfigureSourceChanged(renderer => WorldMaterialBudget.MaterialReady(renderer));
            WorldMaterialBudget.ConfigureAmbientWeight(() => .65f); Center(world);
            Object.DestroyImmediate(ui.targetTexture); Object.DestroyImmediate(cameraGo);
            Object.DestroyImmediate(uiRoot); Object.DestroyImmediate(uiMaterial);
        }
    }
}
