using System;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class TerrainProgram
{
    private static void ConfigurableTerrainGate(GameObject host, MeshRenderer original, Camera camera)
    {
        Material native = original.sharedMaterial;
        Mesh nativeMesh = original.GetComponent<MeshFilter>().sharedMesh;
        float oldWallFade = native.GetFloat("_WallFade_On");
        native.SetFloat("_WallFade_On", 1f);
        var cheapNative = new Material(native)
        {
            // This slot belongs to the external world owner. Terrain-only shader
            // mutants exercise its own cheap route later, and must not mutate the
            // independent owner-slot boundary in this geometry master-gate test.
            shader = Shader.Find("GloomhavenVR/ScenarioCheapTerrain"),
            name = "Fixture current cheap native slot"
        };
        cheapNative.SetFloat("_GHVRTerrainNativeRoute", 3f);
        var map = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
        map.SetPixels(new[] { Color.black, Color.black, Color.black, Color.black }); map.Apply();
        var block = new MaterialPropertyBlock(); block.SetTexture("_TilesOcclusionMap", map);
        block.SetInteger("ToggleWallFade", 1); block.SetFloat("_EnableOcclusionMap", 1f);
        block.SetFloat("_Cutoff", -.15f); block.SetColor("_Tint", Color.cyan);
        bool oldCheap = PerfConfig.CheapWallShadingOn;
        int oldNear = PerfConfig.TerrainDetailPercent, oldFar = PerfConfig.DistantTerrainDetailPercent;
        bool oldShared = PerfConfig.SharedEnvironmentMaterialReadsOn;
        float oldDistance = PerfConfig.TerrainDistanceMeters;
        int oldLayer = original.gameObject.layer, oldMask = camera.cullingMask;
        Component owner = Driver(host);
        FieldInfo pending = owner.GetType().GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo surfaces = owner.GetType().GetField("_surfaces", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo ready = owner.GetType().GetField("_assetsWereReady", BindingFlags.NonPublic | BindingFlags.Instance)!;
        GameObject? pendingNative = null;
        try
        {
            original.gameObject.layer = 28; camera.cullingMask = 1 << 28;
            // Native world-slot ownership is an explicit external boundary here:
            // its current real Unity material stays bound while the complete terrain
            // owner, geometry morph, native MPBs and camera callbacks execute.
            original.sharedMaterial = cheapNative; original.SetPropertyBlock(block);
            ScenarioTerrainBudget.ConfigureCanonicalMaterial(material => material == cheapNative ? native : material);
            ScenarioTerrainBudget.ConfigureWorldMaterialIntegration(material => cheapNative,
                () => new MaterialPass(), () => true, material => material == cheapNative);
            PerfConfig.SharedEnvironmentMaterialReadsOn = true;
            PerfConfig.CheapWallShadingOn = false; PerfConfig.TerrainDetailPercent = 0;
            PerfConfig.DistantTerrainDetailPercent = 27; PerfConfig.TerrainDistanceMeters = 2.1f;
            Morph(host);
            Check(DuringRender(camera, () => original.forceRenderingOff
                && Proxies(host).Exists(proxy => proxy.enabled && proxy.sharedMaterial == cheapNative
                    && Triangles(proxy.GetComponent<MeshFilter>().sharedMesh) < Triangles(original.GetComponent<MeshFilter>().sharedMesh))),
                "terrain master on retains independently selected coarse geometry and current cheap world slots");
            Color on = Pixels(camera)[48 * 96 + 48];
            Check(on.g > .04f && on.b > .04f && on.r < .02f,
                "terrain enabled camera receives current native color on the actual cheap world slot");

            // Interrupt an actual production entry, then switch Off before Update.
            owner.GetType().GetMethod("HandlePreCull", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(owner, new object[] { camera });
            Check(original.forceRenderingOff && ScenarioTerrainBudget.HasCurrentRenderLease(original),
                "terrain master fixture starts with a genuine interrupted current lease");
            PerfConfig.TerrainSubstitutionOn = false;
            Check(!ScenarioTerrainBudget.OwnsRenderSubstitute(original),
                "terrain master off drops prepared ownership immediately before Update");
            TerrainWriteObserver.MaterialReads = 0;
            Check(DuringRender(camera, () => !original.forceRenderingOff
                && !ScenarioTerrainBudget.HasCurrentRenderLease(original) && Proxies(host).TrueForAll(proxy => !proxy.enabled))
                && TerrainWriteObserver.MaterialReads == 0,
                "terrain master off next camera releases interrupted leases without native material preparation");
            Color off = Pixels(camera)[48 * 96 + 48];
            Check(off.g > .04f && off.b > .04f && off.r < .02f
                && original.sharedMaterial == cheapNative && original.GetComponent<MeshFilter>().sharedMesh == nativeMesh,
                "terrain master off keeps original geometry and current cheap native world color");
            block.SetColor("_Tint", Color.magenta); original.SetPropertyBlock(block);
            off = Pixels(camera)[48 * 96 + 48];
            Check(off.r > .04f && off.b > .04f && off.g < .02f,
                "terrain master off retains immediate native property block color writes");
            block.SetFloat("_Cutoff", 1.2f); original.SetPropertyBlock(block);
            Check(Visible(Pixels(camera)) < 50,
                "terrain master off retains native fully dissolved cheap wall output");
            block.SetFloat("_Cutoff", -.15f); original.SetPropertyBlock(block);
            Check(Visible(Pixels(camera)) > 100,
                "terrain master off retains native reappearing cheap wall output");

            // Force pending discovery through the genuine temporary-readiness path.
            PerfConfig.TerrainSubstitutionOn = true; ready.SetValue(owner, false);
            ScenarioTerrainBudget.ConfigureAssetPreparation(() => false, () => false);
            pendingNative = Object.Instantiate(original.gameObject);
            pendingNative.transform.SetParent(original.transform.parent, false);
            pendingNative.GetComponent<MeshRenderer>().enabled = false;
            ScenarioTerrainBudget.QueueRoot(pendingNative); Tick(host);
            Check(((System.Collections.ICollection)pending.GetValue(owner)!).Count > 0,
                "terrain master fixture queues genuine temporarily unavailable native discovery");
            PerfConfig.TerrainSubstitutionOn = false; Tick(host);
            Check(((System.Collections.ICollection)pending.GetValue(owner)!).Count == 0
                && ((System.Collections.ICollection)surfaces.GetValue(owner)!).Count == 0
                && !original.forceRenderingOff && Proxies(host).TrueForAll(proxy => !proxy.enabled),
                "terrain master off Update clears prepared proxies and queued native discovery");
            Check(!PerfConfig.CheapWallShadingOn && PerfConfig.TerrainDetailPercent == 0
                && PerfConfig.DistantTerrainDetailPercent == 27 && PerfConfig.TerrainDistanceMeters == 2.1f,
                "terrain master off preserves every independent detail and shading preference");
            ScenarioTerrainBudget.QueueRoot(original.gameObject);
            Check(((System.Collections.ICollection)pending.GetValue(owner)!).Count == 0,
                "terrain master off rejects new native discovery without queued preparation");
            int beforeOn = MaterialPass.Started; Render(camera);
            Check(MaterialPass.Started == beforeOn && !original.forceRenderingOff,
                "settled terrain master off opens no world read pass or geometry camera work");

            PerfConfig.TerrainSubstitutionOn = true;
            ScenarioTerrainBudget.ConfigureAssetPreparation(() => true, () => false);
            Tick(host);
            Check(DuringRender(camera, () => original.forceRenderingOff
                && Proxies(host).Exists(proxy => proxy.enabled
                    && Triangles(proxy.GetComponent<MeshFilter>().sharedMesh) == Triangles(original.GetComponent<MeshFilter>().sharedMesh))),
                "terrain master reenabled resumes continuous original topology before coarse endpoint");
            Morph(host);
            Check(DuringRender(camera, () => original.forceRenderingOff
                && Proxies(host).Exists(proxy => proxy.enabled
                    && Triangles(proxy.GetComponent<MeshFilter>().sharedMesh) < Triangles(original.GetComponent<MeshFilter>().sharedMesh))),
                "terrain master reenabled reseeds current native walls and saved coarse endpoint");
            Color afterOn = Pixels(camera)[48 * 96 + 48];
            Check(afterOn.r > .04f && afterOn.b > .04f && afterOn.g < .02f,
                "terrain master reenabled immediately inherits changed current native color");
            block.SetFloat("_Cutoff", 1.2f); original.SetPropertyBlock(block);
            Check(Visible(Pixels(camera)) < 50,
                "terrain master reenabled inherits native fully dissolved wall output");
            block.SetFloat("_Cutoff", -.15f); original.SetPropertyBlock(block);
            Check(Visible(Pixels(camera)) > 100,
                "terrain master reenabled preserves native reappearing wall output");
            TerrainWallLeaseBridge(host, original, camera);
        }
        finally
        {
            ScenarioTerrainBudget.BeforeNativeContentChange();
            if (pendingNative != null) Object.DestroyImmediate(pendingNative);
            original.enabled = true; original.forceRenderingOff = false;
            original.SetPropertyBlock(null); original.sharedMaterial = native;
            native.SetFloat("_WallFade_On", oldWallFade);
            original.gameObject.layer = oldLayer; camera.cullingMask = oldMask;
            ScenarioTerrainBudget.ConfigureCanonicalMaterial(material => material);
            ScenarioTerrainBudget.ConfigureWorldMaterialIntegration(material => material, () => new MaterialPass(), () => false, _ => false);
            ScenarioTerrainBudget.ConfigureAssetPreparation(() => true, () => false);
            PerfConfig.TerrainSubstitutionOn = true; PerfConfig.CheapWallShadingOn = oldCheap;
            PerfConfig.TerrainDetailPercent = oldNear; PerfConfig.DistantTerrainDetailPercent = oldFar;
            PerfConfig.TerrainDistanceMeters = oldDistance; PerfConfig.SharedEnvironmentMaterialReadsOn = oldShared;
            Morph(host); Object.DestroyImmediate(cheapNative); Object.DestroyImmediate(map);
        }
    }

    private static void TerrainWallLeaseBridge(GameObject host, MeshRenderer source, Camera camera)
    {
        var hide = new TerrainWallHide(); ScenarioEnvironmentBudget.Writes = 0;
        bool nativeWasLeased = false, released = false;
        DuringRender(camera, () =>
        {
            nativeWasLeased = ScenarioTerrainBudget.HasCurrentRenderLease(source);
            hide.HideByEnable(source);
            released = !ScenarioTerrainBudget.HasCurrentRenderLease(source)
                && Proxies(host).TrueForAll(proxy => !proxy.enabled) && !source.enabled && hide.Owns(source);
            return true;
        });
        Check(nativeWasLeased && released && ScenarioEnvironmentBudget.Writes == 1,
            "actual wall native disable synchronously releases current terrain proxy before its draw");
        hide.HideByEnable(source);
        Check(ScenarioEnvironmentBudget.Writes == 1 && hide.Owns(source),
            "already disabled owned wall with no current terrain consumer remains a write-free noop");
        var foreignHide = new TerrainWallHide(); foreignHide.HideByEnable(source);
        Check(ScenarioEnvironmentBudget.Writes == 1 && !foreignHide.Owns(source),
            "prepared disabled wall without current terrain lease is never adopted or rewritten");
        source.enabled = true;
        bool lateLease = false, lateReleased = false;
        DuringRender(camera, () =>
        {
            // Simulate a native writer which changed enabled/mask without using
            // the bridge: current private consumer ownership survives either edit.
            source.enabled = false; source.forceRenderingOff = false;
            lateLease = ScenarioTerrainBudget.HasCurrentRenderLease(source);
            foreignHide.HideByEnable(source);
            lateReleased = !ScenarioTerrainBudget.HasCurrentRenderLease(source)
                && Proxies(host).TrueForAll(proxy => !proxy.enabled) && !foreignHide.Owns(source);
            return true;
        });
        Check(lateLease && lateReleased && ScenarioEnvironmentBudget.Writes == 2,
            "already native-disabled wall releases its actual terrain consumer without stealing visibility ownership");
        source.enabled = true;
        Check(DuringRender(camera, () => ScenarioTerrainBudget.HasCurrentRenderLease(source)),
            "current native reenable resumes terrain admission after wall owner release");
    }
}
