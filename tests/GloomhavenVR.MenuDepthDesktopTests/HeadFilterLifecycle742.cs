using System;
using System.Collections;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    private sealed partial class Scene
    {
        internal void RetireSpatial742()
        {
            MenuDepthCharacters.Shutdown();
            MenuDepthSources.Unregister(_redDepth);
            MenuDepthSources.Unregister(_greenDepth);
        }
    }

    private static RenderTexture? FilterTarget742() => (RenderTexture?)typeof(MenuDepthHeadRaster)
        .GetField("_filterScratch", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);

    private static bool HasFilterMaterial742() => (Material?)typeof(MenuDepthHeadRaster)
        .GetField("_filterMaterial", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) != null;

    private static void FilterRetired742(string message)
    {
        Check(MenuDepthHeadRaster.FilterBytes == 0 && FilterTarget742() == null && !HasFilterMaterial742(), message);
        foreach (RenderTexture target in Resources.FindObjectsOfTypeAll<RenderTexture>())
            if (target.name == "GloomhavenVR.MenuDepth.HeadAntialias")
                Check(!target.IsCreated(), "retired AA storage has no live GPU color or depth allocation");
    }

    public static IEnumerator HeadFilterLifecycle742(string output, Action<int> complete)
    {
        _count = 0;
        DesktopEyeInput.Available = true;
        WorldUIConfig.CharacterPreviewDepth.Value = false;
        using (var scene = new Scene())
        {
            MenuDepthCharacters.Tick();
            Rect originalRect = scene.Head.rect;
            scene.Head.targetTexture = null;
            scene.Head.pixelRect = new Rect(0, 0, 1280, 720);
            Check(MenuDepthHeadRaster.HeadViewport(scene.Head, new Vector2Int(4040, 4040)) == new Rect(0, 0, 4040, 4040),
                "a null-target XR observation restores the eye grid rather than the desktop pixel rectangle");
            scene.Head.targetTexture = scene.HeadTarget;
            scene.Head.rect = originalRect;
            scene.Head.orthographic = false; scene.Head.fieldOfView = 70f;
            scene.HeadTarget.Release(); scene.HeadTarget.width = 192; scene.HeadTarget.height = 144; scene.HeadTarget.Create();
            scene.Flat.Begin(); scene.Eyes(Vector3.zero);
            var failure = new DesktopFaultSource(scene.SpatialGraphic);
            MenuDepthSources.Register(failure);
            try
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    int before = Time.frameCount; while (before == Time.frameCount) yield return null;
                    scene.Render(false);
                    Check(scene.Flat.Right == null, "a first right-eye failure has no admitted native display material or pair");
                    FilterRetired742("failed first capture releases AA storage even before a display material exists");
                    scene.CheckOriginalState();
                }
                Check(failure.Restores == 6, "each failed first pair restores both begun native source leases");
            }
            finally { MenuDepthSources.Unregister(failure); }

            int frame = Time.frameCount; while (frame == Time.frameCount) yield return null;
            scene.Render(false);
            Check(scene.Flat.Right != null && MenuDepthHeadRaster.FilterBytes > 0,
                "a native graphic-only window owns real antialias storage without a character or Display instance");
            RenderTexture held = FilterTarget742()!;
            MenuDepthSources.SetDiscoveryPump(() => { });
            scene.RetireSpatial742();
            FilterRetired742("the last native graphic releases AA storage while discovery remains installed");
            Check(!held.IsCreated() && scene.Ui.IsCreated() && scene.ModelTarget.IsCreated(),
                "idle AA release preserves original native targets and destroys only its owned allocation");
            MenuDepthSources.SetDiscoveryPump(null);
        }

        using (var scene = new Scene())
        {
            MenuDepthCharacters.Tick();
            scene.Head.orthographic = false; scene.Head.fieldOfView = 70f;
            scene.HeadTarget.Release(); scene.HeadTarget.width = 192; scene.HeadTarget.height = 144; scene.HeadTarget.Create();
            scene.Flat.Begin(); scene.Eyes(Vector3.zero);
            scene.Render(false);
            Check(scene.Flat.Right != null, "native graphics admit the real head coordinator before the shutdown race");
            RenderTexture? borrowed = null;
            bool requested = false;
            Camera.CameraCallback close = camera =>
            {
                if (requested || camera != scene.OriginalA || !MenuDepthHeadRaster.Active) return;
                borrowed = FilterTarget742(); requested = true;
                MenuDepthDisplay.Shutdown();
                Check(borrowed != null && borrowed.IsCreated() && camera.targetTexture == borrowed,
                    "shutdown during a source draw preserves the borrowed target until original restoration");
            };
            Camera.onPreRender += close;
            try
            {
                int frame = Time.frameCount; while (frame == Time.frameCount) yield return null;
                scene.Render(false);
                Check(requested && borrowed != null && !borrowed.IsCreated(),
                    "shutdown requested inside the actual capture drains its AA lease after the native draw");
                FilterRetired742("deferred owner shutdown leaves no retained AA target or material");
                Check(RenderTexture.active != borrowed && scene.OriginalA.targetTexture == scene.Ui
                    && scene.OriginalB.targetTexture == scene.Ui,
                    "deferred release runs after active target and native camera target restoration");
                scene.CheckOriginalState();
            }
            finally { Camera.onPreRender -= close; }
            VRSession.IsRunning = false;
            scene.Flat.End(); scene.RetireSpatial742(); MenuDepthSources.SetDiscoveryPump(null); MenuDepthDisplay.Shutdown();
            FilterRetired742("VR stop releases native graphic-only AA ownership completely");
            VRSession.IsRunning = true;
        }
        WorldUIConfig.CharacterPreviewDepth.Value = true;
        yield return null;
        int surviving = 0;
        foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
            if (material.name == "GloomhavenVR.MenuDepth.HeadResolve") surviving++;
        Check(surviving == 0, "completed close and VR stop retain zero AA materials in the native object registry");
        File.WriteAllText(Path.Combine(output, "lifecycle-counts.txt"), "AA bytes=0; AA material objects=" + surviving + "\n");
        complete(_count);
    }
}
