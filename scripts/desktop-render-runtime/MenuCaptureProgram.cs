using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;

public static class MenuCaptureProgram
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

    public static int Run()
    {
        count = 0;
        var source = new GameObject("MainMenuVideo").AddComponent<Camera>();
        source.orthographic = true; source.orthographicSize = 1; source.aspect = 1;
        source.clearFlags = CameraClearFlags.SolidColor; source.backgroundColor = Color.blue;
        source.cullingMask = -1; source.stereoTargetEye = StereoTargetEyeMask.Both;
        var head = new GameObject("CaptureFixture.Head").AddComponent<Camera>();
        head.cullingMask = VRLayers.ModLayerMask;
        var preview = new GameObject("CaptureFixture.NativePreview").AddComponent<Camera>();
        var target = new RenderTexture(64, 64, 24); target.Create();
        var eye = new RenderTexture(64, 64, 24); eye.Create();
        var foreign = new RenderTexture(64, 64, 24); foreign.Create(); preview.targetTexture = foreign;
        var native = GameObject.CreatePrimitive(PrimitiveType.Quad);
        native.name = "CaptureFixture.NativeArtwork"; native.transform.position = new Vector3(-.5f, 0, 2);
        native.transform.localScale = new Vector3(.5f, .5f, 1);
        var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "GloomhavenVR.FlatScreen"; screen.layer = 27;
        screen.transform.position = new Vector3(.5f, 0, 2); screen.transform.localScale = new Vector3(.5f, .5f, 1);
        var red = new Material(Shader.Find("Unlit/Color")); red.color = Color.red;
        var green = new Material(Shader.Find("Unlit/Color")); green.color = Color.green;
        native.GetComponent<Renderer>().sharedMaterial = red;
        screen.GetComponent<Renderer>().sharedMaterial = green;
        var flat = new FlatScreen();
        VRSession.IsRunning = true; GloomhavenVR.Rig.VRRigDriver.HeadCamera = head;
        VRCameraPolicy.AllowedHead = head;
        int normalCorrections = VRLog.StereoCorrections;
        // Registered AFTER the capture guard: source Update-time protection and an
        // ordinary earlier callback both lose to this causal native render writer.
        Camera.CameraCallback late = camera =>
        {
            if (camera != source) return;
            camera.targetTexture = foreign;
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
        };
        try
        {
            // No periodic sweep runs between this late native discovery and Camera.Render.
            flat.Capture(source, target);
            Camera.onPreCull += late;
            for (int pose = 0; pose < 4; pose++)
            {
                screen.transform.rotation = Quaternion.Euler(0, pose * 10, 0);
                source.stereoTargetEye = StereoTargetEyeMask.Both; // native late writer
                source.targetTexture = foreign; // native late writer after Update capture
                source.cullingMask = pose % 2 == 0 ? -1 : (1 | VRLayers.ModLayerMask);
                int originalMask = source.cullingMask;
                source.Render();
                Check(source.targetTexture == target,
                    "render-time capture routing repairs a late native target reset");
                Check(source.stereoTargetEye == StereoTargetEyeMask.None,
                    "render-time capture excludes native stereo without waiting for a periodic sweep");
                Check(source.cullingMask == originalMask,
                    "native current mask is restored after each captured render");
                Color kept = Pixel(target, 16, 32), excluded = Pixel(target, 48, 32);
                Check(kept.r > .8f && kept.g < .1f,
                    "captured original native artwork remains visible");
                Check(excluded.b > .8f && excluded.g < .1f,
                    "captured native menu cannot redraw the floating screen");
            }
            Check(VRLog.StereoCorrections - normalCorrections == 1,
                "repeated native stereo corrections keep normal logging bounded");
            Check(preview.targetTexture == foreign && preview.cullingMask == -1,
                "independent preview capture retains its own target and mask");
            head.targetTexture = eye; head.orthographic = true; head.orthographicSize = 1; head.aspect = 1;
            head.Render();
            Color visible = Pixel(eye, 48, 32);
            Check(visible.g > .8f && visible.r < .1f && head.cullingMask == VRLayers.ModLayerMask,
                "head still renders the original floating screen");
            flat.InterruptCapture(source);
            Check(source.cullingMask == 1, "interrupted capture holds only the current native mask");
            flat.InterruptCapture(head);
            Check(source.cullingMask == (1 | VRLayers.ModLayerMask),
                "next camera repairs a missing capture post-render");
            flat.InterruptCapture(source); flat.End();
            Check(source.cullingMask == (1 | VRLayers.ModLayerMask) && source.stereoTargetEye == StereoTargetEyeMask.Both,
                "capture teardown restores interrupted mask and original stereo identity");
        }
        finally
        {
            Camera.onPreCull -= late;
            flat.End(); source.targetTexture = null; head.targetTexture = null; preview.targetTexture = null;
            foreach (RenderTexture rt in new[] { target, eye, foreign }) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            foreach (UnityEngine.Object obj in new UnityEngine.Object[] { source.gameObject, head.gameObject, preview.gameObject, native, screen, red, green })
                UnityEngine.Object.DestroyImmediate(obj);
            GloomhavenVR.Rig.VRRigDriver.HeadCamera = null;
        }
        return count;
    }
}
