using System;
using GloomhavenVR.Core;
using UnityEngine;

public static partial class EnvironmentProgram
{
    // Execute the production bridge against actual Unity references and chunks.
    // The new material owner is an explicit delegate boundary in this suite;
    // its production final-cull lifecycle has a separate world-runtime fixture.
    private static void WorldMaterialConsumerIntegration()
    {
        using var room = new Room();
        MeshRenderer first = room.Floor(1f), second = room.Floor(2f);
        var variant = new Material(room.Original) { name = "Fixture.WorldPrivateMaterial" };
        bool world = true;
        int beforeWrites = 0, beforeContent = 0, ready = 0;
        ScenarioEnvironmentBudget.ConfigureWorldMaterialIntegration(_ => { }, _ => ready++,
            renderer => { beforeWrites++; if (renderer.sharedMaterial == variant) renderer.sharedMaterial = room.Original; },
            () => beforeContent++, material => material == variant ? room.Original : material,
            material => material == variant, () => world);
        try
        {
            Configure(true, true, 100);
            first.sharedMaterial = second.sharedMaterial = variant;
            ScenarioEnvironmentBudget.WorldMaterialChanged(first);
            ScenarioEnvironmentBudget.WorldMaterialChanged(second);
            ScenarioEnvironmentBudget.BeforeLoadingComplete();
            Check(first.sharedMaterial == variant && second.sharedMaterial == variant && beforeWrites == 0 && ready == 0,
                "world reference notification retains new bindings without recursively restoring its owner");
            Check(ScenarioEnvironmentBudget.CanonicalMaterial(variant) == room.Original,
                "composed canonical material maps private world references to exact native original");
            Check(room.Chunks().Length == 1 && room.Chunks()[0].sharedMaterial == variant,
                "world material owner supplies exact floor chunk without legacy material rewrite");
            Tick("HandlePreCull", room.Camera);
            Check(first.forceRenderingOff && second.forceRenderingOff,
                "world-owned floor references retain existing exact geometry submission");
            ScenarioEnvironmentBudget.BeforeWorldMaterialDisposal();
            Check(!first.forceRenderingOff && !second.forceRenderingOff && room.Chunks().Length == 0,
                "world variant disposal synchronously releases current and queued material consumers");
            ScenarioEnvironmentBudget.BeforeNativeRendererWrite(first);
            Check(beforeWrites == 1 && first.sharedMaterial == room.Original && second.sharedMaterial == variant,
                "native write bridge restores only the written world renderer before native access");
            ScenarioEnvironmentBudget.BeforeNativeContentChange();
            Check(beforeContent == 1, "native content boundary reaches world clone restoration before continuation");
            first.sharedMaterial = second.sharedMaterial = variant;
            ScenarioEnvironmentBudget.WorldMaterialChanged(first);
            ScenarioEnvironmentBudget.WorldMaterialChanged(second);
            ScenarioEnvironmentBudget.BeforeLoadingComplete();
            var unsupported = new Material(room.Original) { name = "Fixture.ForeignMaterial" };
            try
            {
                first.sharedMaterial = unsupported;
                ScenarioEnvironmentBudget.WorldMaterialChanged(first);
                Check(!first.forceRenderingOff && !second.forceRenderingOff,
                    "world refusal revokes entire earlier chunk while preserving foreign slot replacement");
                Check(first.sharedMaterial == unsupported,
                    "world change bridge cannot overwrite a foreign current native material");
            }
            finally { first.sharedMaterial = room.Original; UnityEngine.Object.DestroyImmediate(unsupported); }
            world = false;
            first.sharedMaterial = second.sharedMaterial = room.Original;
            Configure(false, false, 100); Tick();
        }
        finally
        {
            first.sharedMaterial = second.sharedMaterial = room.Original;
            ScenarioEnvironmentBudget.ConfigureWorldMaterialIntegration(_ => { }, _ => { }, _ => { },
                () => { }, material => material, _ => false, () => false);
            UnityEngine.Object.DestroyImmediate(variant);
        }
    }
}
