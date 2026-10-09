using System;
using System.IO;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;

public static class MotionLayout660
{
    private static int assertions;
    private static Camera camera = null!;
    private static Texture2D readback = null!;
    private static RenderTexture target = null!;
    private static void Paint(Image ink, Vector3 expected, string message)
    {
        Canvas.ForceUpdateCanvases();
        Check(ink.gameObject.activeInHierarchy && ink.enabled && ink.canvas.isActiveAndEnabled
            && ink.color.a > 0f && ink.canvasRenderer.GetInheritedAlpha() > 0f, message + " remains enabled");
        camera.Render(); RenderTexture.active = target;
        readback.ReadPixels(new Rect(0f, 0f, 512f, 512f), 0, 0); readback.Apply();
        Vector3 pixel = camera.WorldToViewportPoint(expected);
        Color painted = readback.GetPixel((int)(pixel.x * 512f), (int)(pixel.y * 512f));
        Check(painted.g > .5f && painted.b > .5f, message + " paints at its authored centre on every render");
        RenderTexture.active = null;
    }
    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }
    private static RectTransform Rect(string name, Transform parent = null)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false); rect.sizeDelta = new Vector2(500f, 600f);
        return rect;
    }
    public static void Run(string output)
    {
        RectTransform parent = Rect("Actual nonuniform native canvas");
        parent.SetPositionAndRotation(new Vector3(.2f, .7f, -.3f), Quaternion.Euler(0f, 41f, 0f));
        parent.localScale = new Vector3(.001f, .0017f, .001f);
        Canvas canvas = parent.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        camera = new GameObject("Exact native panel camera").AddComponent<Camera>(); camera.enabled = false;
        camera.transform.SetPositionAndRotation(parent.position - parent.forward * 2f, parent.rotation);
        camera.orthographic = true; camera.orthographicSize = .7f; camera.nearClipPlane = .01f;
        camera.backgroundColor = Color.black; camera.clearFlags = CameraClearFlags.SolidColor;
        target = new RenderTexture(512, 512, 24); target.Create(); camera.targetTexture = target;
        readback = new Texture2D(512, 512, TextureFormat.RGBA32, false);
        RectTransform root = Rect("Original button panel", parent);
        root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
        root.pivot = new Vector2(.5f, .5f); root.localPosition = new Vector3(37f, -91f, 0f);
        root.sizeDelta = new Vector2(180f, 50f);
        Image rootInk = root.gameObject.AddComponent<Image>(); rootInk.color = Color.cyan;
        var motion = new TownServiceMotion(parent, new Transform[] {root}, "enhance.confirm.part.native|");
        motion.BeforeApply(0f); motion.AfterApply(0f, .1f, sourceSampleTime: 0f);
        Vector3 original = root.localPosition;
        motion.BeforeApply(1f);
        root.anchorMin = new Vector2(.2f, .8f); root.anchorMax = new Vector2(.8f, .8f);
        root.pivot = new Vector2(.2f, .9f); root.sizeDelta = new Vector2(65f, 63f);
        // Production Binding.ApplyRootLayout retains local position, then the
        // authored header places this root. The motion pass must retain both.
        root.localPosition = original;
        motion.AfterApply(1f, .1f, sourceSampleTime: 1f);
        for (int frame = 0; frame <= 12; frame++)
        {
            motion.Tick(1f + frame / 120f);
            Check(Vector3.Distance(root.localPosition, original) < .001f,
                "native layout interpolation preserves the separately authored root position on every render");
            Paint(rootInk, parent.TransformPoint(original), "the continuously visible original button panel");
            if (frame == 0 || frame == 6 || frame == 12)
                File.WriteAllBytes(Path.Combine(output, "native-layout-" + frame + ".png"), readback.EncodeToPNG());
        }
        // Actual observer motion includes the enclosing detached canvas first.
        // A census retirement reparents that canvas, not its native root child.
        RectTransform host = Rect("Detached original canvas", parent);
        host.localPosition = new Vector3(10f, 15f, 0f);
        RectTransform native = Rect("Original canvas content", host);
        native.sizeDelta = new Vector2(90f, 25f);
        Image retainedInk = native.gameObject.AddComponent<Image>(); retainedInk.color = Color.cyan;
        host.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var retained = new TownServiceMotion(host, new Transform[] {native}, "enhance.confirm.part.native|");
        retained.BeforeApply(0f); retained.AfterApply(0f, .1f, sourceSampleTime: 0f);
        Vector3 oldFrom = host.localPosition, oldTo = oldFrom + Vector3.right * 70f;
        retained.BeforeApply(2f); host.localPosition = oldTo;
        retained.AfterApply(2f, .1f, sourceSampleTime: 2f); retained.Tick(2.04f);
        RectTransform survivor = Rect("Retained census parent");
        survivor.SetPositionAndRotation(new Vector3(-.3f, .4f, .1f), Quaternion.Euler(0f, -29f, 0f));
        survivor.localScale = new Vector3(.0018f, .0021f, .0009f);
        Matrix4x4 oldBasis = parent.localToWorldMatrix;
        Vector3 retainedWorld = host.position;
        retained.Reparent(survivor);
        Check(Vector3.Distance(host.position, retainedWorld) < .000001f,
            "retained census reparent preserves the exact current native picture immediately");
        for (int frame = 0; frame <= 12; frame++)
        {
            float now = 2.04f + frame / 120f;
            retained.Tick(now);
            Vector3 expected = oldBasis.MultiplyPoint3x4(Vector3.LerpUnclamped(oldFrom, oldTo, Mathf.Clamp01((now - 2f) / .11f)));
            Check(Vector3.Distance(host.position, expected) < .000001f,
                "retained original continues its exact authored world path after a census mount retires");
            Paint(retainedInk, expected, "the original button panel during holder retirement");
            if (frame == 0 || frame == 6 || frame == 12)
                File.WriteAllBytes(Path.Combine(output, "native-retirement-" + frame + ".png"), readback.EncodeToPNG());
        }
        retained.BeforeApply(2.2f);
        Check(Vector3.Distance(host.position, oldBasis.MultiplyPoint3x4(oldTo)) < .000001f,
            "a delayed child-only property pass does not restore the retired parent's old target");
        File.WriteAllText(Path.Combine(output, "assertions.txt"), assertions + " assertions\n");
        UnityEngine.Object.DestroyImmediate(parent.gameObject);
        UnityEngine.Object.DestroyImmediate(survivor.gameObject);
        UnityEngine.Object.DestroyImmediate(camera.gameObject);
        UnityEngine.Object.DestroyImmediate(readback); UnityEngine.Object.DestroyImmediate(target);
    }
}
