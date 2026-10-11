using System;
using GloomhavenVR.Core;
using UnityEngine;

public static class FrameMrPresentationProgram
{
    private static int _count;
    private static void Check(bool value, string message)
    { _count++; if (!value) throw new Exception(message); }

    private static Color Pixel(Camera camera, int x, int y)
    {
        camera.Render();
        RenderTexture old = RenderTexture.active;
        var image = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
        try
        {
            RenderTexture.active = camera.targetTexture;
            image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); image.Apply();
            return image.GetPixel(x, y);
        }
        finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(image); }
    }

    public static int Run()
    {
        _count = 0;
        var headRoot = new GameObject("Fixture.Head");
        var otherRoot = new GameObject("Fixture.UIcamera");
        var skyRoot = new GameObject("Fixture.SkyOwnership");
        var cardRoot = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var shader = Shader.Find("Unlit/Color");
        Check(shader != null && shader.isSupported, "actual Unity graphics shader available");
        var material = new Material(shader); material.color = Color.blue;
        cardRoot.GetComponent<Renderer>().sharedMaterial = material;
        cardRoot.transform.position = new Vector3(0, 0, 2);
        var sky = skyRoot.AddComponent<MeshRenderer>();
        var skyMaterial = new Material(shader);
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        target.Create();
        Camera head = headRoot.AddComponent<Camera>(); head.enabled = false;
        head.targetTexture = target; head.fieldOfView = 60; head.allowHDR = true;
        head.clearFlags = CameraClearFlags.SolidColor;
        Color original = new(.12f, .08f, .22f, 1f); head.backgroundColor = original;
        Camera other = otherRoot.AddComponent<Camera>(); other.enabled = false;
        other.clearFlags = CameraClearFlags.Skybox; other.allowHDR = true;
        Color otherOriginal = other.backgroundColor;
        VRCameraPolicy.AllowedHead = head; VRCameraPolicy.Others = new[] { head, other };
        MixedReality.SkyFixture = sky; RenderSettings.skybox = skyMaterial;
        try
        {
            FrameNativePassthrough.Required = true;
            FrameNativePassthrough.Ready = false; MixedReality.Enabled.Value = true;
            MixedReality.Tick();
            Check(head.backgroundColor == original && head.allowHDR, "pending native request leaves ordinary camera intact");
            Check(RenderSettings.skybox == skyMaterial && sky.enabled, "unsupported/pending Frame preserves ordinary environment");
            Check(!MixedReality.BackingsWanted, "unsupported/pending Frame has no MR backing");
            Check(MixedReality.Enabled.Value, "incompatible saved preference is retained");

            FrameNativePassthrough.Ready = true; MixedReality.Tick();
            Check(head.backgroundColor == Color.clear, "accepted native Frame clear is transparent black");
            Check(!head.allowHDR, "native head avoids RGB-only HDR intermediates");
            Check(other.allowHDR, "other cameras' HDR permissions are untouched");
            Check(!sky.enabled && RenderSettings.skybox == null, "native MR suppresses sky only after acceptance");
            Check(MixedReality.BackingsWanted, "accepted native MR allows existing UI backings");
            Color clear = Pixel(head, 2, 2), face = Pixel(head, 32, 32);
            Check(clear.a < .01f && clear.r < .01f && clear.g < .01f && clear.b < .01f,
                "actual rendered native background has zero RGBA");
            Check(face.b > .95f && face.a > .99f, "actual rendered opaque game geometry retains color and alpha");
            MixedReality.Tick();
            Check(head.backgroundColor == Color.clear, "repeat native tick keeps transparent clear");

            MixedReality.Enabled.Value = false; MixedReality.Tick();
            Check(head.backgroundColor == original && head.allowHDR, "MR off restores original head color and HDR");
            Check(other.clearFlags == CameraClearFlags.Skybox && other.backgroundColor == otherOriginal && other.allowHDR,
                "MR off restores secondary camera without taking its HDR ownership");
            Check(sky.enabled && RenderSettings.skybox == skyMaterial, "MR off restores original sky");
            Check(!MixedReality.BackingsWanted && !FrameNativePassthrough.IsActive, "MR off ends native blend ownership");

            FrameNativePassthrough.Required = false; FrameNativePassthrough.Ready = false;
            MixedReality.KeyColor.Value = new Color(0, 1, 0, .2f); MixedReality.Enabled.Value = true;
            int requests = FrameNativePassthrough.Requests; MixedReality.Tick();
            Check(FrameNativePassthrough.Requests == requests, "PC chromakey never requests native Frame composition");
            Check(head.backgroundColor == Color.green && head.allowHDR, "PC key remains opaque green with original HDR");
            clear = Pixel(head, 2, 2); face = Pixel(head, 32, 32);
            Check(clear.g > .95f && clear.a > .99f, "actual PC chromakey pixels remain opaque green");
            Check(face.b > .95f && face.a > .99f, "PC geometry presentation remains opaque");
            Check(MixedReality.BackingsWanted, "existing PC backings remain enabled");
            MixedReality.RestoreAll();

            FrameNativePassthrough.Required = true; FrameNativePassthrough.Ready = true;
            MixedReality.Tick(); VRSession.IsRunning = false; MixedReality.Tick();
            Check(head.backgroundColor == original && head.allowHDR && sky.enabled, "XR stop restores active presentation");
            Check(!MixedReality.BackingsWanted && !FrameNativePassthrough.IsActive, "XR stop leaves no native MR ownership");
            VRSession.IsRunning = true; FrameNativePassthrough.Ready = false;
            MixedReality.Tick();
            Check(head.backgroundColor == original && sky.enabled, "XR restart pending keeps normal environment");
            FrameNativePassthrough.Ready = true; MixedReality.Tick();
            UnityEngine.Object.DestroyImmediate(headRoot); VRCameraPolicy.AllowedHead = null;
            MixedReality.RestoreAll();
            Check(other.clearFlags == CameraClearFlags.Skybox && sky.enabled, "destroyed head does not prevent surviving-camera/sky restoration");
            MixedReality.RestoreAll();
            Check(other.clearFlags == CameraClearFlags.Skybox, "repeated restoration is idempotent");
            return _count;
        }
        finally
        {
            MixedReality.RestoreAll(); RenderSettings.skybox = null;
            foreach (var obj in new UnityEngine.Object[] { headRoot, otherRoot, skyRoot, cardRoot, material, skyMaterial, target })
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
