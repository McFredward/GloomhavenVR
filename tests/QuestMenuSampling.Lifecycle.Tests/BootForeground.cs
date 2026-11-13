using System;
using System.IO;
using System.Reflection;
using GloomhavenVR.WorldUI;
using UnityEngine;

// Actual production native-art material/mesh, native Camera.Render, GPU depth
// and draw queues. No XR provider or final Android eye picture is claimed.
internal static class BootForegroundComposition
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    internal static void Probe(Camera head, LoadingIndicator indicator, Action<bool, string> check)
    {
        var root = (GameObject)typeof(LoadingIndicator).GetField("_root", Hidden)!.GetValue(indicator)!;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        Renderer basis = renderers[0];
        Material owned = basis.sharedMaterial;
        var sprite = new Material(Shader.Find("Sprites/Default")) { mainTexture = owned.mainTexture };
        bool overlayEnabled = renderers[1].enabled;
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Quad);
        blocker.name = "retained-depth-or-late-empty-menu-capture";
        blocker.layer = basis.gameObject.layer;
        blocker.transform.position = head.transform.TransformPoint(new Vector3(0, -.18f, 1));
        blocker.transform.rotation = head.transform.rotation;
        blocker.transform.localScale = Vector3.one * .7f;
        var opaque = new Material(Shader.Find("GloomhavenVR/Overlay")) { color = Color.black };
        opaque.SetFloat("_VertexColor", 0); opaque.SetInt("_ZWrite", 1);
        opaque.SetInt("_SrcBlend", 1); opaque.SetInt("_DstBlend", 0); opaque.renderQueue = 2000;
        blocker.GetComponent<Renderer>().sharedMaterial = opaque;
        blocker.SetActive(false);
        Color tint = owned.color;
        float oldFov = head.fieldOfView, oldAspect = head.aspect;
        try
        {
            renderers[1].enabled = false;
            int maxDifference = 0;
            foreach (float alpha in new[] { .1f, .5f, 1f })
            {
                owned.color = sprite.color = new Color(tint.r, tint.g, tint.b, alpha);
                basis.sharedMaterial = sprite; Color32[] before = Pixels(head);
                basis.sharedMaterial = owned; Color32[] after = Pixels(head);
                for (int p = 0; p < before.Length; p++)
                    for (int channel = 0; channel < 4; channel++)
                        maxDifference = Math.Max(maxDifference, Math.Abs(before[p][channel] - after[p][channel]));
            }
            check(maxDifference <= 2, "Initial foreground retains original native RGB and alpha at all Sprite edges");
            owned.color = sprite.color = new Color(tint.r, tint.g, tint.b, 1);
            blocker.SetActive(true);
            basis.sharedMaterial = sprite;
            int oldDepth = Visible(Pixels(head));
            basis.sharedMaterial = owned;
            int newDepth = Visible(Pixels(head));
            check(oldDepth == 0 && newDepth > 3,
                "Initial Quest spinner survives real retained depth and later blank capture composition");
            opaque.SetInt("_ZWrite", 0); opaque.renderQueue = 4000;
            basis.sharedMaterial = sprite; int oldLate = Visible(Pixels(head));
            basis.sharedMaterial = owned; int newLate = Visible(Pixels(head));
            check(oldLate == 0 && newLate > 3,
                "Initial foreground renders after a later blank menu capture without camera or viewport writes");
            blocker.SetActive(false);
            head.fieldOfView = 110; head.aspect = 2728f / 2858f;
            MethodInfo crop = typeof(FlatScreen).GetMethod("CropQuestProbeToRenderer", Hidden)!;
            MethodInfo draw = typeof(FlatScreen).GetMethod("RenderQuestConsumerProbe", Hidden)!;
            Matrix4x4 view = head.worldToCameraMatrix, projection = head.projectionMatrix;
            int wideMinimum = int.MaxValue, tightMinimum = int.MaxValue;
            Quaternion rotation = basis.transform.localRotation;
            for (int step = 0; step < 12; step++)
            {
                basis.transform.localRotation = rotation * Quaternion.Euler(0, 0, step * 30);
                Matrix4x4 tight = (Matrix4x4)crop.Invoke(null, new object[] { basis, view, projection })!;
                wideMinimum = Math.Min(wideMinimum, ProbePixels(draw, basis, owned, view, projection));
                tightMinimum = Math.Min(tightMinimum, ProbePixels(draw, basis, owned, view, tight));
            }
            basis.transform.localRotation = rotation;
            check(tightMinimum > 30 && tightMinimum > wideMinimum * 8,
                "Bounds-fit source probe resolves the native thin ring independently of a three-pixel full-eye sample");
            string? report = Environment.GetEnvironmentVariable("QUEST_LOADING_FRAME_REPORT");
            if (!string.IsNullOrEmpty(report)) File.WriteAllText(Path.Combine(report, "initial-foreground.json"),
                "{\"nativeSpriteCompared\":true,\"maxChannelDifference\":" + maxDifference
                + ",\"baselineDepthPixels\":" + oldDepth + ",\"foregroundDepthPixels\":" + newDepth
                + ",\"baselineLateCapturePixels\":" + oldLate + ",\"foregroundLateCapturePixels\":" + newLate
                + ",\"fullEyeMinimum\":" + wideMinimum + ",\"boundsFitMinimum\":" + tightMinimum
                + ",\"actualCameraRender\":true,\"xrProvider\":false,\"headsetProven\":false}\n");
        }
        finally
        {
            head.fieldOfView = oldFov; head.aspect = oldAspect;
            basis.sharedMaterial = owned; owned.color = tint; renderers[1].enabled = overlayEnabled;
            UnityEngine.Object.DestroyImmediate(blocker); UnityEngine.Object.DestroyImmediate(opaque); UnityEngine.Object.DestroyImmediate(sprite);
        }
    }

    private static int ProbePixels(MethodInfo draw, Renderer renderer, Material material, Matrix4x4 view, Matrix4x4 projection)
    {
        var target = (RenderTexture)draw.Invoke(null, new object[] { renderer, material, view, projection })!;
        try { return Visible(Read(target)); }
        finally { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
    }
    private static Color32[] Pixels(Camera camera) { camera.Render(); return Read(camera.targetTexture); }
    private static int Visible(Color32[] pixels)
    {
        int visible = 0;
        foreach (Color32 pixel in pixels) if (pixel.r > 6 || pixel.g > 6 || pixel.b > 6) visible++;
        return visible;
    }
    private static Color32[] Read(RenderTexture target)
    {
        RenderTexture previous = RenderTexture.active;
        var copy = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
        try
        {
            RenderTexture.active = target;
            copy.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false); copy.Apply(false);
            return copy.GetPixels32();
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(copy); }
    }
}
