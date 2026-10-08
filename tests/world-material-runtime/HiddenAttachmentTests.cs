using System;
using System.IO;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class WorldMaterialProgram
{
    private static void HiddenAttachmentWrites(MeshRenderer source, Material material, Func<string, int> pixels)
    {
        var block = new MaterialPropertyBlock();
        PerfConfig.SkipHiddenWallAttachmentWritesOn = true;
        using (var hide = new WallSegmentFade.AttachmentFixture())
        {
            source.SetPropertyBlock(null); source.enabled = true; WorldMaterialBudget.MaterialReady(source); pixels(null!);
            Check(WorldMaterialBudget.IsOwnedVariant(source.sharedMaterial),
                "actual hide fixture starts with a current private world material lease");
            hide.Attach(source); hide.Hide(source);
            Check(!source.enabled && source.sharedMaterial == material,
                "actual enabled wall hide releases the current private world material before native setter");
            Check(hide.Show(source), "actual enabled hide retains its own enable restitution");
        }
        using (var fade = new WallSegmentFade.AttachmentFixture())
        {
            fade.Attach(source); fade.Present(1f); fade.Hide(source);
            Check(!source.enabled && pixels(null!) == 0, "actual enabled hide installs its native endpoint before camera presentation");
            var foreign = new MaterialPropertyBlock(); foreign.SetColor("_Tint", Color.magenta); foreign.SetFloat("_Cutoff", .91f);
            foreign.SetFloat("_ForeignNativeEffect", .43f); source.SetPropertyBlock(foreign);
            ScenarioEnvironmentBudget.Writes = 0; fade.Hide(source);
            Check(ScenarioEnvironmentBudget.Writes == 0, "repeated enabled-hide endpoint has no native write release");
            ScenarioEnvironmentBudget.Writes = 0;
            for (int frame = 0; frame < 60; frame++) { fade.Present(1f); fade.Hide(source); }
            source.GetPropertyBlock(block);
            Check(ScenarioEnvironmentBudget.Writes == 0 && block.GetColor("_Tint") == Color.magenta
                && block.GetFloat("_ForeignNativeEffect") == .43f && block.GetFloat("_Cutoff") == .91f,
                "fully hidden owned endpoint avoids all repeated native effect writes");
            Check(pixels("hidden-writes-on") == 0 && !source.enabled, "untouched hidden foreign MPB cannot paint an owned disabled attachment");
            PerfConfig.SkipHiddenWallAttachmentWritesOn = false;
            ScenarioEnvironmentBudget.Writes = 0;
            for (int frame = 0; frame < 60; frame++) { fade.Present(1f); fade.Hide(source); }
            Check(ScenarioEnvironmentBudget.Writes == 60, "option off preserves every original continuous native attachment drive");
            Check(pixels("hidden-writes-off") == 0, "option off retains the same actual hidden camera endpoint");
            PerfConfig.SkipHiddenWallAttachmentWritesOn = true;
            ScenarioEnvironmentBudget.Writes = 0; fade.Present(.5f); source.GetPropertyBlock(block);
            Check(ScenarioEnvironmentBudget.Writes == 1 && Mathf.Approximately(block.GetFloat("_Cutoff"), .475f),
                "intermediate hidden return continues native opacity delivery");
            source.enabled = true; ScenarioEnvironmentBudget.Writes = 0;
            source.SetPropertyBlock(foreign); fade.Present(1f); source.GetPropertyBlock(block);
            Check(ScenarioEnvironmentBudget.Writes == 1 && block.GetTexture(Shader.PropertyToID("_TilesOcclusionMap")) != null
                && Mathf.Approximately(block.GetFloat("_Cutoff"), .5f) && source.sharedMaterial == material && pixels(null!) == 0,
                "actual native reenable immediately restores current full native fade drive");
            fade.Hide(source); fade.Present(0f); fade.Show(source);
            Check(source.enabled && pixels("hidden-writes-returned") > 500, "first attachment return drives authored appearance before owned enable");
            fade.Present(1f); fade.Hide(source); fade.Guard(source, false, true);
            ScenarioEnvironmentBudget.Writes = 0; fade.Present(1f);
            Check(source.enabled && !source.HasPropertyBlock() && ScenarioEnvironmentBudget.Writes > 0 && pixels(null!) > 500,
                "new local or remote held guard restores owned hidden attachment before endpoint skip");
            fade.Guard(source, false, false); fade.Present(1f); fade.Hide(source); fade.Guard(source, true, false);
            ScenarioEnvironmentBudget.Writes = 0; fade.Present(.5f);
            Check(ScenarioEnvironmentBudget.Writes == 0, "live floor guard remains ahead of dormant and native attachment delivery");
            fade.Guard(source, false, false); fade.Present(0f); fade.Show(source); fade.Restore();
        }
        using (var fade = new WallSegmentFade.AttachmentFixture())
        {
            fade.Attach(source); source.enabled = false; fade.Hide(source);
            ScenarioEnvironmentBudget.Writes = 0; fade.Present(1f); source.GetPropertyBlock(block);
            Check(ScenarioEnvironmentBudget.Writes == 1 && Mathf.Approximately(block.GetFloat("_Cutoff"), .5f),
                "foreign disabled renderer without owned hide still drives its native endpoint");
            Check(!fade.Show(source) && !source.enabled,
                "foreign disabled renderer never acquires our hide ownership or a foreign enable");
            source.enabled = true; source.forceRenderingOff = true; ScenarioEnvironmentBudget.Writes = 0;
            fade.Present(1f); Check(ScenarioEnvironmentBudget.Writes == 1,
                "foreign forceRenderingOff mask alone cannot authorize dormant attachment skip");
            source.forceRenderingOff = false; fade.Restore();
        }
        HiddenParticleWrites();
        PerfConfig.SkipHiddenWallAttachmentWritesOn = false;
        string evidence = Environment.GetEnvironmentVariable("GHVR_WORLD_EVIDENCE")!;
        File.WriteAllText(Path.Combine(evidence, typeof(WorldMaterialProgram).Assembly.GetName().Name + "-hidden-write-counts.txt"),
            "60 stable owned hidden ticks: On=0 native bridge/effect writes; Off=60; repeated Hide=0.\n"
            + "Actual enabled hide, native re-enable, every intermediate return and held restitution retain native writes.\n"
            + "Counts are actual source-extracted Unity seam operations; not headset FPS or timing.\n");
    }
    private static void HiddenParticleWrites()
    {
        var go = new GameObject("Actual dormant native particle attachment");
        var system = go.AddComponent<ParticleSystem>(); system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main; main.startColor = Color.white; main.startSizeMultiplier = 1f;
        var emission = system.emission; emission.rateOverTimeMultiplier = 10f;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        using var fade = new WallSegmentFade.AttachmentFixture();
        try
        {
            fade.Attach(renderer);
            fade.Present(.25f);
            Check(Mathf.Approximately(system.main.startColor.color.a, .75f)
                && Mathf.Approximately(system.main.startSizeMultiplier, .7875f)
                && Mathf.Approximately(system.emission.rateOverTimeMultiplier, 7.5f),
                "actual particle attachment retains intermediate authored module curve");
            fade.Present(1f); fade.Hide(renderer);
            main = system.main; main.startColor = new Color(1, 1, 1, .44f); main.startSizeMultiplier = 1.1f;
            emission = system.emission; emission.rateOverTimeMultiplier = 7f;
            for (int i = 0; i < 60; i++) fade.Present(1f);
            Check(Mathf.Approximately(system.main.startColor.color.a, .44f)
                && Mathf.Approximately(system.main.startSizeMultiplier, 1.1f)
                && Mathf.Approximately(system.emission.rateOverTimeMultiplier, 7f),
                "owned fully hidden particle endpoint skips native module rewrites");
            fade.Present(.5f);
            Check(Mathf.Approximately(system.main.startColor.color.a, .5f)
                && Mathf.Approximately(system.main.startSizeMultiplier, .575f)
                && Mathf.Approximately(system.emission.rateOverTimeMultiplier, 5f),
                "particle return immediately resumes its unchanged smooth module curve");
            fade.Present(0f); fade.Show(renderer);
            Check(renderer.enabled && system.main.startColor.color == Color.white
                && system.main.startSizeMultiplier == 1f && system.emission.rateOverTimeMultiplier == 10f,
                "particle attachment restores authored module state before owned reappearance");
        }
        finally { fade.Dispose(); Object.DestroyImmediate(go); }
    }
}
