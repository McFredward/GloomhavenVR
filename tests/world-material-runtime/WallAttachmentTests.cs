using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class WorldMaterialProgram
{
    private static void WallAttachmentParity()
    {
        BundleShaders.AttachmentPixels = true;
        // Run() completed the previous plugin owner's teardown and removed its patches.
        VRSession.Harmony = new HarmonyLib.Harmony("world.material.attachments." + typeof(WorldMaterialProgram).Assembly.GetName().Name);
        var host = new GameObject("Attachment world owner");
        var scenario = new GameObject("Attachment native scenario"); scenario.AddComponent<ProceduralScenario>();
        var room = new GameObject("Attachment native room"); room.transform.SetParent(scenario.transform, false);
        room.AddComponent<ProceduralMapTile>();
        if (room.GetComponent<ProceduralStyle>() == null) room.AddComponent<ProceduralStyle>();
        if (room.GetComponent<ApparanceEntity>() == null) room.AddComponent<ApparanceEntity>();
        var generated = new GameObject("Generated Content"); generated.transform.SetParent(room.transform, false);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh mesh = cube.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(cube);
        var material = new Material(Shader.Find("Amp_Basic_WallFade"));
        material.SetColor("_Tint", new Color(.8f, .4f, .2f, 1f)); material.SetFloat("_Cutoff", .5f);
        // A valid native simplex sample exposes several clip areas for this surrogate;
        // the production world shader executes its actual simplex function separately.
        material.SetFloat("_NativeNoise", 0f);
        material.SetTexture("_MainTex", Texture2D.whiteTexture);
        var source = Source("CR_ST_Shelves_Stone_Wood native attachment", generated.transform, mesh, material);
        source.transform.position = new Vector3(0, 1.5f, 0); source.transform.localScale = new Vector3(1, .01f, 1);
        source.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        source.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        var cameraGo = new GameObject("Actual attachment camera"); var camera = cameraGo.AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = .6f;
        camera.transform.position = new Vector3(0, 8, 0); camera.transform.rotation = Quaternion.Euler(90, 0, 0);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.allowHDR = false;
        camera.targetTexture = new RenderTexture(64, 64, 24); camera.targetTexture.Create();
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = Color.white;
        WorldMaterialBudget.ConfigureSourceChanged(_ => { }); WorldMaterialBudget.ConfigureBeforeVariantDisposal(() => { });
        WorldMaterialBudget.ConfigureAmbientWeight(() => 1f);
        Shader.SetGlobalInteger("ToggleWallFade", 0);
        string evidence = Environment.GetEnvironmentVariable("GHVR_WORLD_EVIDENCE")!;
        string prefix = typeof(WorldMaterialProgram).Assembly.GetName().Name + "-attachment";
        int Pixels(string name)
        {
            camera.Render(); var image = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            RenderTexture old = RenderTexture.active; RenderTexture.active = camera.targetTexture;
            image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); image.Apply();
            int pixels = image.GetPixels32().Count(pixel => pixel.r > 8);
            if (name != null) File.WriteAllBytes(Path.Combine(evidence, prefix + "-" + name + ".png"), image.EncodeToPNG());
            RenderTexture.active = old; Object.DestroyImmediate(image); return pixels;
        }
        try
        {
            WorldMaterialBudget.Install(host);
            foreach (int mode in new[] { 0, 2 })
            {
                PerfConfig.WorldMaterialQualityMode = mode; Tick(host, 16); WorldMaterialBudget.MaterialReady(source);
                source.SetPropertyBlock(null); Pixels(null!);
                Material current = source.sharedMaterial;
                File.AppendAllText(Path.Combine(evidence, prefix + "-ownership.txt"),
                    "mode=" + mode + "; current=" + current.shader.name + "; original=" + material.shader.name
                    + "; keywords=" + string.Join(",", material.shaderKeywords) + "; factory=" + WorldMaterialBudget.VariantFor(material).shader.name
                    + "; log=" + string.Join(" | ", VRLog.Messages.TakeLast(8)) + "\n");
                Check(mode == 0 ? current == material : WorldMaterialBudget.IsOwnedVariant(current),
                    "attachment A/B uses the actual requested world owner and private production shader");
                using var fade = new WallSegmentFade.AttachmentFixture();
                Check(fade.Capable(source) && fade.Named(source),
                    "optimized native wall admission resolves original shader name and live gate");
                Check(fade.Predict(source) == 0, "optimized fade prediction uses native priority before union-schema alpha");
                fade.Attach(source);
                Check(fade.Native && fade.ColorChannel == -1,
                    "optimized attachment retains original native dissolve classification");
                Check(fade.DonorShader == material.shader, "attachment template donor retains the actual authored shader");
                var outward = new List<int>(); var returning = new List<int>();
                foreach (float value in new[] { 0f, .125f, .25f, .5f, .75f, .9375f, 1f })
                {
                    fade.Present(value);
                    Check(source.sharedMaterial == material,
                        "attachment native write releases original renderer binding before its effect");
                    int pixels = Pixels(value == 0 ? "mode" + mode + "-solid" : value == .5f ? "mode" + mode + "-mid" : value == 1f ? "mode" + mode + "-held" : null!);
                    outward.Add(pixels);
                    Check(source.enabled && !source.forceRenderingOff && source.GetComponent<MeshFilter>().sharedMesh == mesh,
                        "native attachment keeps live original geometry during its continuous fade");
                    var block = new MaterialPropertyBlock(); source.GetPropertyBlock(block);
                    File.AppendAllText(Path.Combine(evidence, prefix + "-diagnostic.txt"),
                        "mode=" + mode + "; fade=" + value + "; pixels=" + pixels + "; shader=" + source.sharedMaterial.shader.name
                        + "; integer=" + block.GetInteger(Shader.PropertyToID("ToggleWallFade")) + "; float=" + block.GetFloat(Shader.PropertyToID("_ToggleWallfade"))
                        + "; cutoff=" + block.GetFloat(Shader.PropertyToID("_Cutoff")) + "; map=" + block.GetTexture(Shader.PropertyToID("_TilesOcclusionMap")).name + "\n");
                    Check(block.GetTexture(Shader.PropertyToID("_TilesOcclusionMap")) != null
                        && Mathf.Approximately(block.GetFloat(Shader.PropertyToID("_Cutoff")), value == 1f ? .5f : Mathf.Lerp(-.05f, 1f, value)),
                        "actual attachment drive retains native continuous map and cutoff on every phase");
                }
                Check(outward[0] > 500 && outward.Last() == 0 && outward.Distinct().Count() > 2,
                    "native and optimized attachment render a visible continuous transition and hidden upper endpoint");
                foreach (float value in new[] { .9375f, .75f, .5f, .25f, .125f, 0f })
                { fade.Present(value); returning.Add(Pixels(value == 0 ? "mode" + mode + "-returned" : null!)); }
                Check(returning.Last() == outward[0], "native and optimized attachment return to original solid pixels");
                File.WriteAllText(Path.Combine(evidence, prefix + "-mode" + mode + "-curve.txt"),
                    "OUT=" + string.Join(",", outward) + "; IN=" + string.Join(",", returning) + "\n");
                source.transform.position = Vector3.zero; fade.Present(1f);
                Check(Pixels("mode" + mode + "-foundation") > 500,
                    "native and optimized dissolve retain their solid foundation geometry");
                source.transform.position = new Vector3(0, 1.5f, 0);
                fade.Restore(); Check(!source.HasPropertyBlock() && source.sharedMaterial == material,
                    "attachment unfade restores real original slot and removes owned effects before continuation");
            }
            PerfConfig.WorldMaterialQualityMode = 2; Tick(host, 16); Pixels(null!);
            HiddenAttachmentWrites(source, material, Pixels);
            Material privateDonor = source.sharedMaterial;
            var toggled = Native("Native N_MRAO keyword admission");
            try
            {
                toggled.EnableKeyword("_WALLFADE_ON_ON"); source.sharedMaterial = toggled; Pixels(null!);
                using (var fade = new WallSegmentFade.AttachmentFixture())
                {
                    Check(fade.Capable(source), "current optimized N_MRAO native keyword admits its live original gate");
                    fade.Attach(source); Check(fade.Native && fade.DonorShader == toggled.shader,
                        "current optimized N_MRAO native keyword retains native attachment and template identity");
                }
                toggled.DisableKeyword("_WALLFADE_ON_ON"); Pixels(null!);
                using (var fade = new WallSegmentFade.AttachmentFixture())
                {
                    Check(!fade.Capable(source), "next-eye native keyword opt-out cannot inherit private shader capability");
                    fade.Attach(source); Check(!fade.Native && fade.ColorChannel == -1 && fade.Predict(source) == 2,
                        "authored native gated-off material retains its original cutoff channel without forced dissolve");
                }
                toggled.SetFloat("_WallFade_On", 1f); Pixels(null!);
                using (var fade = new WallSegmentFade.AttachmentFixture())
                {
                    fade.Attach(source); Check(fade.Native,
                        "fresh in-place native float gate regains its native attachment path on the next eye");
                }
            }
            finally { source.sharedMaterial = material; Object.DestroyImmediate(toggled); Pixels(null!); privateDonor = source.sharedMaterial; }
            using (var fade = new WallSegmentFade.AttachmentFixture())
            {
                source.sharedMaterials = new[] { privateDonor, null! };
                Check(fade.Predict(source) == 2, "half-built additional slot preserves the original first-slot own-channel prediction");
                fade.Attach(source); Check(!fade.Native && fade.Originals == null,
                    "half-built additional slot never swaps an existing native cutoff channel");
                source.sharedMaterials = new[] { privateDonor };
            }
            using (var fade = new WallSegmentFade.AttachmentFixture())
            {
                fade.Donor(privateDonor);
                Check(fade.DonorShader == material.shader,
                    "a current private template donor never supplies substitute shader or default native route");
                var plain = new Material(Shader.Find("Unlit/Texture"));
                try
                {
                    source.sharedMaterials = new[] { plain, privateDonor }; fade.Attach(source);
                    Check(fade.Native && fade.Originals != null && fade.Originals[0] == plain && fade.Originals[1] == material,
                        "mixed attachment restore snapshot retains real native slots");
                    fade.Restore(); Check(source.sharedMaterials[0] == plain && source.sharedMaterials[1] == material,
                        "mixed attachment swap restores original native material count order and identities");
                }
                finally { source.sharedMaterials = new[] { material }; Object.DestroyImmediate(plain); }
            }
            foreach (bool floor in new[] { true, false })
            {
                using var fade = new WallSegmentFade.AttachmentFixture();
                fade.Guard(source, floor, !floor); fade.Attach(source); fade.Present(1f);
                Check(!fade.Native && !source.HasPropertyBlock() && Pixels(null!) > 500,
                    "attachment native primitive continues to refuse protected floor and held renderer writes");
            }
            // Unsupported original programs fail open even if a previously owned reference is still present.
            material.EnableKeyword("_ENABLE_ANIM"); Pixels(null!);
            Check(source.sharedMaterial == material, "unsupported native effect program remains native after next-eye restoration");
            material.DisableKeyword("_ENABLE_ANIM"); Pixels(null!);
            Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial), "current native supported program can regain its private world shading");
            PerfConfig.WorldMaterialQualityMode = 0; Tick(host);
            Check(source.sharedMaterial == material && !source.HasPropertyBlock(), "world option off retains restored native attachment rendering");
        }
        finally
        {
            WorldMaterialBudget.Shutdown(); BundleShaders.AttachmentPixels = false;
            VRSession.Harmony.UnpatchSelf();
            Object.DestroyImmediate(scenario); Object.DestroyImmediate(host);
            Object.DestroyImmediate(camera.targetTexture); Object.DestroyImmediate(cameraGo); Object.DestroyImmediate(material);
            PerfConfig.WorldMaterialQualityMode = 2;
        }
    }
}
