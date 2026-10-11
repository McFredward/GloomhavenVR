using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;

public static class LateMenuProgram
{
    private static int count;
    private static void Check(bool condition, string message)
    { count++; if (!condition) throw new InvalidOperationException(message); }
    private static Color Pixel(RenderTexture target, int x, int y)
    {
        RenderTexture? previous = RenderTexture.active;
        var read = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target;
            read.ReadPixels(new Rect(x, y, 1, 1), 0, 0); read.Apply();
            return read.GetPixel(0, 0);
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(read); }
    }
    private static Camera CameraFor(string name)
    {
        var camera = new GameObject(name).AddComponent<Camera>();
        camera.aspect = 1; camera.fieldOfView = 60;
        camera.clearFlags = CameraClearFlags.Depth; camera.backgroundColor = Color.blue;
        camera.cullingMask = -1; camera.stereoTargetEye = StereoTargetEyeMask.Both;
        return camera;
    }
    private static RenderTexture Texture()
    { var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32); target.Create(); return target; }

    public static int Run()
    {
        count = 0;
        var target = Texture(); var glass = Texture(); var eye = Texture(); var foreign = Texture();
        var head = CameraFor("LateMenuFixture.Head"); head.targetTexture = eye; head.cullingMask = VRLayers.ModLayerMask;
        var preview = CameraFor("LateMenuFixture.NativePreview"); preview.targetTexture = foreign;
        var modPreview = CameraFor("GloomhavenVR.MapAlbedoCamera"); modPreview.targetTexture = foreign;
        var manual = CameraFor("LateMenuFixture.ManualCamera"); manual.enabled = false;
        var inactive = CameraFor("LateMenuFixture.InactiveCamera"); inactive.gameObject.SetActive(false);
        Camera? source = null, ui = null;
        var native = GameObject.CreatePrimitive(PrimitiveType.Quad);
        native.name = "LateMenuFixture.NativeArtwork"; native.transform.position = new Vector3(-.5f, 0, 2);
        native.transform.localScale = new Vector3(.5f, .5f, 1);
        var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "GloomhavenVR.FlatScreen"; screen.layer = 27;
        screen.transform.position = new Vector3(.5f, 0, 2); screen.transform.localScale = new Vector3(.5f, .5f, 1);
        var red = new Material(Shader.Find("Unlit/Color")); red.color = Color.red;
        var green = new Material(Shader.Find("Unlit/Color")); green.color = Color.green;
        native.GetComponent<Renderer>().sharedMaterial = red; screen.GetComponent<Renderer>().sharedMaterial = green;
        var flat = new FlatScreen(); VRSession.IsRunning = true;
        GloomhavenVR.Rig.VRRigDriver.HeadCamera = head; VRCameraPolicy.AllowedHead = head;
        Camera.CameraCallback late = camera =>
        {
            if (camera != source && camera != ui) return;
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
            camera.cullingMask = -1; // native writer after the previous Update census
        };
        try
        {
            // Guard is live while the actual captured stack is still empty. All
            // sources below are rendered without an intervening Update census.
            flat.BeginEmptyCapture(target);
            Camera.onPreCull += late;
            head.targetTexture = null; head.Render(); head.targetTexture = eye;
            head.Render(); preview.Render(); modPreview.Render(); manual.Render();
            flat.InterruptCapture(inactive); // Unity refuses to Render an inactive object.
            Check(flat.MemberCount == 0 && flat.CaptureScans == 0,
                "head foreign preview and manual-disabled cameras never trigger first-render census");
            Check(head.targetTexture == eye && head.stereoTargetEye == StereoTargetEyeMask.Both
                && head.cullingMask == VRLayers.ModLayerMask,
                "unknown head camera retains exact eye target stereo and mask");
            Check(preview.targetTexture == foreign && modPreview.targetTexture == foreign
                && preview.clearFlags == CameraClearFlags.Depth && preview.cullingMask == -1,
                "unknown native and mod preview cameras retain independent RT ownership");
            Check(manual.targetTexture == null && !manual.enabled && manual.cullingMask == -1
                && manual.stereoTargetEye == StereoTargetEyeMask.Both && inactive.targetTexture == null,
                "disabled manual and inactive native cameras keep their original ownership");

            // MainMenuVideo is created/enabled after the menu's initial census.
            int reports = VRLog.FirstCaptureReports;
            VRLog.WantsDebug = false;
            source = CameraFor("MainMenuVideo"); source.tag = "MainCamera";
            Matrix4x4 projection = source.projectionMatrix;
            source.Render();
            Check(FlatScreen.IsCaptured(source) && source.targetTexture == target && flat.MemberCount == 1,
                "late native camera is captured before its first actual render");
            Check(source.stereoTargetEye == StereoTargetEyeMask.None && source.cullingMask == -1,
                "first native render excludes stereo and restores its current native mask");
            Check(source.clearFlags == CameraClearFlags.SolidColor && source.backgroundColor == Color.black,
                "first native render applies current opaque stack base clear");
            Check(source == Camera.main && source.enabled && source.projectionMatrix == projection,
                "first-render capture retains native camera and exact projection identity");
            Color kept = Pixel(target, 16, 32), excluded = Pixel(target, 48, 32);
            Check(kept.r > .8f && kept.g < .1f && excluded.g < .1f && excluded.r < .1f,
                "first native render preserves artwork and excludes floating screen feedback");
            Check(flat.CaptureScans == 1, "first native render performs exactly one rare capture census");
            Check(VRLog.FirstCaptureReports == reports,
                "first-render discovery diagnostic remains absent outside Debug");
            VRLog.WantsDebug = true;

            // Native writers may reset target/stereo each render. Settled known
            // cameras must continue to use640's constant-time final guard only.
            for (int pose = 0; pose < 4; pose++)
            {
                screen.transform.rotation = Quaternion.Euler(0, pose * 10, 0);
                source.targetTexture = null; source.stereoTargetEye = StereoTargetEyeMask.Both;
                source.Render();
                Check(source.targetTexture == target && source.stereoTargetEye == StereoTargetEyeMask.None
                    && source.cullingMask == -1 && flat.CaptureScans == 1,
                    "settled native renders keep first-render repair without repeated census");
            }
            // A game replacement RT wins while the guard is released; ReleaseStack
            // must restore only our target, even for an adopted first-render camera.
            source.targetTexture = foreign; flat.End();
            Check(source.targetTexture == foreign && source.clearFlags == CameraClearFlags.Depth
                && source.backgroundColor == Color.blue && source.stereoTargetEye == StereoTargetEyeMask.Both,
                "first-render release preserves a newer game-owned replacement target");
            source.targetTexture = null;

            // Scenario->menu style re-entry and late UI creation exercise actual
            // split routing/base policy, plus the synchronously invoked stereo
            // construction boundary. Stereo pixel rendering itself is inherited.
            flat.BeginEmptyCapture(target, glass); flat.EnableStereoFixture();
            source.Render();
            Check(source.targetTexture == target && flat.StereoSyncCount == 1,
                "first late background camera synchronizes current stereo stack");
            ui = CameraFor("LateMenuFixture.UI Camera"); ui.orthographic = true; ui.orthographicSize = 1; ui.depth = 2;
            ui.tag = "UICamera";
            ui.Render();
            Check(ui.targetTexture == glass && FlatScreen.IsCaptured(ui),
                "first late UI render joins transparent glass in the same render");
            Check(ui.clearFlags == CameraClearFlags.SolidColor && ui.backgroundColor == Color.clear
                && ui.stereoTargetEye == StereoTargetEyeMask.None && ui.cullingMask == -1,
                "first late UI render applies transparent base and original native mask");
            Check(flat.StereoSyncCount == 1 && flat.MemberCount == 2,
                "late UI capture synchronizes native background only without UI stereo clone");
            kept = Pixel(glass, 16, 32); excluded = Pixel(glass, 48, 32);
            Check(kept.r > .8f && kept.g < .1f && excluded.a < .1f && excluded.g < .1f,
                "first late UI render draws native artwork with transparent undrawn glass");
            int scans = flat.CaptureScans;
            source.Render(); ui.Render(); head.Render(); preview.Render(); modPreview.Render(); manual.Render();
            Check(flat.CaptureScans == scans,
                "settled split menu and independent cameras avoid extra discovery scans");
            Check(VRLog.FirstCaptureReports == reports + 2,
                "Debug first-render discovery reports once per newly captured camera");
            flat.End();
            Check(source.targetTexture == null && ui.targetTexture == null
                && source.clearFlags == CameraClearFlags.Depth && ui.clearFlags == CameraClearFlags.Depth
                && source.backgroundColor == Color.blue && ui.backgroundColor == Color.blue
                && source.cullingMask == -1 && ui.cullingMask == -1,
                "first-render capture releases exact camera and native clear state");
            Check(source.stereoTargetEye == StereoTargetEyeMask.Both && ui.stereoTargetEye == StereoTargetEyeMask.Both,
                "VR teardown restores stereo identity for first-render native and UI cameras");
        }
        finally
        {
            Camera.onPreCull -= late; flat.End();
            VRLog.WantsDebug = true;
            foreach (Camera camera in new[] { head, preview, modPreview, manual, inactive })
            { camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(camera.gameObject); }
            if (source != null) { source.targetTexture = null; UnityEngine.Object.DestroyImmediate(source.gameObject); }
            if (ui != null) { ui.targetTexture = null; UnityEngine.Object.DestroyImmediate(ui.gameObject); }
            foreach (RenderTexture rt in new[] { target, glass, eye, foreign })
            { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            foreach (UnityEngine.Object obj in new UnityEngine.Object[] { native, screen, red, green })
                UnityEngine.Object.DestroyImmediate(obj);
            GloomhavenVR.Rig.VRRigDriver.HeadCamera = null;
        }
        return count;
    }
}
