using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Board.FigureGrab;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class TerrainProgram
{
    private static void PropOwnershipPasses(GameObject host, MeshRenderer original, Camera camera, List<MeshRenderer> clones)
    {
        var roots = new GameObject[16];
        for (int i = 0; i < roots.Length; i++) roots[i] = new GameObject("Unrelated registered prop " + i);
        var foreign = new GameObject("Nested terrain ownership camera").AddComponent<Camera>();
        foreign.enabled = false; foreign.cullingMask = 0;
        foreign.targetTexture = new RenderTexture(16, 16, 16); foreign.targetTexture.Create();
        Transform parent = clones[0].transform.parent;
        var changedRoot = new GameObject("Current registered visual"); changedRoot.transform.SetParent(parent, false);
        var nestedRoot = new GameObject("Current remote visual"); nestedRoot.transform.SetParent(parent, false);
        try
        {
            PropGrab.SetRoots(roots); HeldProps.SetRoots(); NetHeldProps.SetRoots();
            PerfConfig.SharedEnvironmentMaterialReadsOn = false; TerrainOwnershipObserver.Reset();
            Check(DuringRender(camera, () => original.forceRenderingOff && clones.TrueForAll(r => r.forceRenderingOff)),
                "independent terrain scope retains all ordinary walls with unrelated registered props");
            int legacyReads = TerrainOwnershipObserver.VisualReads;
            PerfConfig.SharedEnvironmentMaterialReadsOn = true; TerrainOwnershipObserver.Reset();
            Check(DuringRender(camera, () => original.forceRenderingOff && clones.TrueForAll(r => r.forceRenderingOff))
                && TerrainOwnershipObserver.VisualReads == roots.Length
                && TerrainOwnershipObserver.RootCopies >= 1 && TerrainOwnershipObserver.RootCopies <= 2
                && legacyReads >= 96 * roots.Length,
                "terrain shared scope reads exact prop visuals once per camera instead of every ancestor");
            Debug.Log("Terrain actual source API visual reads (96 walls,16 unrelated props): shared="
                + TerrainOwnershipObserver.VisualReads + ", independent=" + legacyReads + ".");

            HeldProps.SetRoots(clones[0].gameObject);
            Check(DuringRender(camera, () => !clones[0].forceRenderingOff && original.forceRenderingOff),
                "unregistered local held terrain root remains native at the next actual camera");
            NetHeldProps.SetRoots(clones[1].gameObject);
            Check(DuringRender(camera, () => !clones[1].forceRenderingOff && original.forceRenderingOff),
                "unregistered remote held terrain root remains native at the next actual camera");
            PerfConfig.SharedEnvironmentMaterialReadsOn = false;
            Check(DuringRender(camera, () => !clones[0].forceRenderingOff && !clones[1].forceRenderingOff
                && original.forceRenderingOff),
                "independent terrain scope retains unregistered local and remote held roots");
            HeldProps.SetRoots(); NetHeldProps.SetRoots(); PerfConfig.SharedEnvironmentMaterialReadsOn = true;
            var currentRoots = (GameObject[])roots.Clone(); currentRoots[0] = clones[2].gameObject;
            PropGrab.SetRoots(currentRoots);
            Check(DuringRender(camera, () => !clones[2].forceRenderingOff && original.forceRenderingOff),
                "new same-count grabbable visual between cameras remains native");
            PropGrab.SetRoots(roots);
            Check(DuringRender(camera, () => clones[2].forceRenderingOff),
                "removed current grabbable root restores eligible terrain at the next camera");

            bool wrote = false;
            ScenarioTerrainBudget.ConfigureCanonicalMaterial(material =>
            {
                if (!wrote)
                {
                    wrote = true; currentRoots[0] = changedRoot; PropGrab.SetRoots(currentRoots);
                    clones[0].transform.SetParent(changedRoot.transform, true);
                    ScenarioTerrainBudget.BeforeNativeRendererWrite(clones[0]);
                }
                return material;
            });
            Check(DuringRender(camera, () => wrote && !clones[0].forceRenderingOff && original.forceRenderingOff),
                "native writer registration and reparent invalidate same-pass terrain roots");
            ScenarioTerrainBudget.ConfigureCanonicalMaterial(material => material);
            clones[0].transform.SetParent(parent, true); PropGrab.SetRoots(roots);

            // Actual nested Render reaches the production foreign-camera recovery.
            // The native registry/reparent change happens inside that nested callback,
            // after the outer source already initialized its synchronous root map.
            bool nested = false;
            Camera.CameraCallback inside = rendered =>
            {
                if (rendered != foreign) return;
                NetHeldProps.SetRoots(nestedRoot); clones[1].transform.SetParent(nestedRoot.transform, true);
            };
            Camera.onPreCull += inside;
            try
            {
                ScenarioTerrainBudget.ConfigureCanonicalMaterial(material =>
                { if (!nested) { nested = true; foreign.Render(); } return material; });
                Check(DuringRender(camera, () => nested && !clones[1].forceRenderingOff && original.forceRenderingOff),
                    "nested native camera registration and reparent refresh outer terrain ownership");
                Check(!original.forceRenderingOff && clones.TrueForAll(r => !r.forceRenderingOff)
                    && Proxies(host).TrueForAll(r => !r.enabled),
                    "nested terrain material lookup ends with every native source unmasked");
            }
            finally { Camera.onPreCull -= inside; ScenarioTerrainBudget.ConfigureCanonicalMaterial(material => material); }
            clones[1].transform.SetParent(parent, true); NetHeldProps.SetRoots();
            original.enabled = false;
            Check(DuringRender(camera, () => !ScenarioTerrainBudget.HasCurrentRenderLease(original))
                && !original.forceRenderingOff,
                "native disabled terrain stays original after shared ownership reuse");
            original.enabled = true;
        }
        finally
        {
            ScenarioTerrainBudget.ConfigureCanonicalMaterial(material => material);
            ScenarioTerrainBudget.BeforeNativeContentChange();
            clones[0].transform.SetParent(parent, true); clones[1].transform.SetParent(parent, true);
            original.enabled = true; PropGrab.SetRoots(); HeldProps.SetRoots(); NetHeldProps.SetRoots();
            PerfConfig.SharedEnvironmentMaterialReadsOn = true;
            foreign.targetTexture.Release(); Object.DestroyImmediate(foreign.targetTexture); Object.DestroyImmediate(foreign.gameObject);
            Object.DestroyImmediate(changedRoot); Object.DestroyImmediate(nestedRoot);
            foreach (GameObject root in roots) Object.DestroyImmediate(root);
        }
    }
}
