using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;

public static class InteractionProgram
{
    private static int count;
    private static void Check(bool condition, string message)
    { count++; if (!condition) throw new InvalidOperationException(message); }
    private static Camera Make(string name)
    {
        var camera = new GameObject(name).AddComponent<Camera>();
        camera.cullingMask = 0x123456;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.red;
        return camera;
    }
    public static int Run()
    {
        count = 0;
        VRSession.IsRunning = true;
        var native = Make("DesktopFixture.Native");
        native.tag = "MainCamera";
        native.clearFlags = CameraClearFlags.Skybox;
        var head = Make("DesktopFixture.Head");
        GloomhavenVR.Rig.VRRigDriver.HeadCamera = head;
        var preview = Make("DesktopFixture.OwnPreview");
        var privateTarget = new RenderTexture(32, 32, 24); privateTarget.Create();
        preview.targetTexture = privateTarget;
        var flat = new FlatScreen();
        try
        {
            flat.Tick(false, false);
            Check(flat.Sink != null && native.targetTexture == flat.Sink,
                "mirror off still redirects discarded native rendering");
            Check(head.targetTexture == null && preview.targetTexture == privateTarget,
                "head eyes and native preview capture stay outside the scrub");
            Check(native.enabled && Camera.main == native,
                "native Camera.main and callbacks stay enabled");
            Check(native.cullingMask == 0x123456,
                "native gameplay observes its original culling mask between renders");

            int before = -1, after = -1;
            CameraClearFlags beforeClear = CameraClearFlags.Nothing, afterClear = CameraClearFlags.Nothing;
            Camera.CameraCallback pre = c => { if (c == native) { before = c.cullingMask; beforeClear = c.clearFlags; } };
            Camera.CameraCallback post = c => { if (c == native) { after = c.cullingMask; afterClear = c.clearFlags; } };
            Camera.onPreCull += pre; Camera.onPostRender += post;
            try { native.Render(); }
            finally { Camera.onPreCull -= pre; Camera.onPostRender -= post; }
            Check(before == 0 && after == 0x123456,
                "actual built-in Camera.Render skips draw and restores within its own callback pair");
            Check(native.cullingMask == 0x123456, "actual render cannot leave a zero gameplay mask");
            Check(beforeClear == CameraClearFlags.SolidColor && afterClear == CameraClearFlags.Skybox,
                "unused native skybox draw is suppressed within the same real render callback pair");

            flat.Pre(native);
            Check(native.cullingMask == 0, "manual callback models an interrupted native render");
            flat.Pre(head);
            Check(native.cullingMask == 0x123456 && head.cullingMask == 0x123456,
                "next camera repairs a missing post-render without masking the head");

            flat.Pre(native);
            native.cullingMask = 0; // deliberately interrupted render
            flat.Tick(true, false);
            Check(native.cullingMask == 0x123456 && native.targetTexture == null,
                "visible headset menu restores scrub ownership before native capture");
            flat.Capture(native, privateTarget);
            flat.Tick(true, false);
            Check(native.targetTexture == privateTarget && native.cullingMask == 0x123456,
                "black spectator does not blank visible in-headset 2D menu capture");
            flat.Pre(native);
            Check(native.cullingMask == 0x123456, "captured menu camera cannot enter draw skip");
            flat.ForgetCapture(native);
            flat.Tick(false, true);
            Check(native.targetTexture == flat.Sink, "mirror on preserves the same always-suppressed native draw");
            flat.Tick(false, false);
            Check(native.targetTexture == flat.Sink, "live spectator toggle never restores unused native draw");

            flat.Pre(native);
            flat.ReleaseForCapture();
            Check(native.cullingMask == 0x123456 && native.targetTexture == null,
                "same-frame Show handoff restores camera before CaptureStack sees it");
            flat.Capture(native, privateTarget);
            flat.Tick(false, false);
            Check(native.targetTexture == privateTarget, "foreign capture ownership is never redirected");
            flat.ForgetCapture(native);
            flat.Tick(false, false);
            var replacement = new RenderTexture(16, 16, 16); replacement.Create();
            native.targetTexture = replacement;
            flat.Tick(false, false);
            Check(native.targetTexture == replacement, "game-owned replacement target survives scrub release");
            flat.End();
            Check(native.targetTexture == replacement, "release restores only a target still owned by the scrub");
            native.targetTexture = null; replacement.Release(); UnityEngine.Object.DestroyImmediate(replacement);

            flat.Tick(false, false);
            flat.Pre(native);
            VRSession.IsRunning = false;
            flat.Tick(false, false);
            Check(native.cullingMask == 0x123456 && native.targetTexture == null,
                "VR stop restores native backbuffer and interrupted render mask");
            VRSession.IsRunning = true;
            flat.Tick(false, false);
            RenderTexture.active = privateTarget;
            flat.OnEndOfFrame();
            Check(RenderTexture.active == privateTarget,
                "black desktop clear restores the previous in-headset capture target");
            flat.Tick(false, true);
            flat.OnEndOfFrame();
            Check(RenderTexture.active == privateTarget,
                "left-eye spectator never overwrites or rebinds headset capture");
            VRSession.IsRunning = false;
            flat.OnEndOfFrame();
            Check(RenderTexture.active == privateTarget, "flat mode leaves its current native target untouched");
        }
        finally
        {
            RenderTexture.active = null;
            flat.End();
            preview.targetTexture = null;
            privateTarget.Release(); UnityEngine.Object.DestroyImmediate(privateTarget);
            UnityEngine.Object.DestroyImmediate(native.gameObject);
            UnityEngine.Object.DestroyImmediate(head.gameObject);
            UnityEngine.Object.DestroyImmediate(preview.gameObject);
            GloomhavenVR.Rig.VRRigDriver.HeadCamera = null;
        }
        return count + ConversionMeasureProgram.Run();
    }
}
