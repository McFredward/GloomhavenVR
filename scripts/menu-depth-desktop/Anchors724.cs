using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[ExecuteInEditMode]
public sealed class MenuDepthAnchorsImageEffect724 : MonoBehaviour
{
    public int Draws;
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    { Draws++; Graphics.Blit(source, destination); }
}

public static partial class MenuDepthDesktopProgram
{
    // The native model target alone cannot establish a fixed VR cabinet. Observe
    // the full original camera -> RawImage -> UI stack -> physical glass -> head
    // raster, then compare against a fixed world landmark calibrated WITHOUT eyes.
    public static IEnumerator Anchors724(string output, Action<int> complete) => Anchors724Core(output, complete, false, false);
    public static IEnumerator AnchorsModelEffect724(string output, Action<int> complete) => Anchors724Core(output, complete, true, false);
    public static IEnumerator AnchorsUiEffect724(string output, Action<int> complete) => Anchors724Core(output, complete, false, true);

    private static IEnumerator Anchors724Core(string output, Action<int> complete, bool modelEffect, bool uiEffect)
    {
        _count = 0; GloomhavenVR.Core.VRSession.IsRunning = true; DesktopEyeInput.Available = true;
        using (var scene = new Scene())
        {
            Transform holder = scene.ModelCamera.GetComponent<Character3DDisplayManager>().Holder;
            foreach (Transform child in holder) child.gameObject.SetActive(false);
            holder.SetParent(scene.ModelCamera.transform, false);
            holder.localPosition = new Vector3(.4f, -.45f, 2.56f); holder.localRotation = Quaternion.identity;
            // Reuse the source-owned fixture material so Scene.Dispose owns it
            // even when a causal control interrupts the iterator with a failure.
            Material material = holder.GetComponentInChildren<MeshRenderer>(true).sharedMaterial; material.color = Color.cyan;
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere); marker.layer = 8;
            marker.transform.SetParent(holder, false); marker.transform.localPosition = new Vector3(-.3f, .4f, .6f);
            marker.transform.localScale = Vector3.one * .4f; marker.GetComponent<MeshRenderer>().sharedMaterial = material;
            Transform actor = new GameObject("native main-actor ground placement").transform;
            actor.SetParent(holder, false); actor.position = scene.ModelCamera.transform.TransformPoint(new Vector3(.1f, -1.25f, 2.56f));
            marker.transform.SetParent(actor, true);
            var manager = scene.ModelCamera.GetComponent<Character3DDisplayManager>(); manager.BindOriginal(actor);
            Check(manager.HasOriginal, "the calibrated cabinet uses the native original models CharacterManager identity path");
            // A camera child unrelated to Character3D.models must not inflate the
            // authored body envelope. The real source camera excludes its layer.
            var unrelated = GameObject.CreatePrimitive(PrimitiveType.Cube); unrelated.layer = 9;
            unrelated.transform.SetParent(holder, false); unrelated.transform.localPosition = new Vector3(10, 0, 0);
            unrelated.GetComponent<MeshRenderer>().sharedMaterial = material;
            RawImage raw = Object.FindObjectsOfType<RawImage>().Single(image => ReferenceEquals(image.texture, scene.ModelTarget));
            // The old miniature UI fixture had only70px in the character pane.
            // Supply a native-like280px aperture while retaining physical layout.
            scene.NativePixels725(4);
            scene.ModelTarget.Release(); scene.ModelTarget.width = scene.ModelTarget.height = 1024; scene.ModelTarget.Create();
            raw.uvRect = new Rect(.15f, .12f, .7f, .76f);
            scene.ModelCamera.rect = new Rect(.1f, .05f, .8f, .9f);
            scene.OriginalA.rect = new Rect(.08f, .12f, .83f, .76f);
            scene.ModelCamera.allowHDR = false; scene.ModelCamera.allowMSAA = false;
            scene.OriginalA.allowHDR = false; scene.OriginalA.allowMSAA = false;
            if (modelEffect) scene.ModelCamera.gameObject.AddComponent<MenuDepthAnchorsImageEffect724>();
            if (uiEffect) scene.OriginalA.gameObject.AddComponent<MenuDepthAnchorsImageEffect724>();
            scene.Head.orthographic = false; scene.Head.fieldOfView = 55;
            scene.HeadTarget.Release(); scene.HeadTarget.width = 960; scene.HeadTarget.height = 720; scene.HeadTarget.Create();
            scene.Screen.SetPositionAndRotation(new Vector3(.3f, .2f, 3), Quaternion.Euler(0, 25, 0));
            scene.Screen.localScale = new Vector3(2.4f, 1.5f, 1);
            scene.Flat.Begin(); Canvas.ForceUpdateCanvases();
            scene.ModelCamera.Render();
            if (modelEffect) Check(scene.ModelCamera.GetComponent<MenuDepthAnchorsImageEffect724>().Draws > 0,
                "the native-style model image effect is actually invoked by Camera.Render");
            Texture2D nativeProbe = Read(scene.ModelTarget, Path.Combine(output, "724-native-viewport-probe.png"));
            Vector2 nativeRaster = CyanCentre(nativeProbe); Object.DestroyImmediate(nativeProbe);
            Vector3 nativeViewport = scene.ModelCamera.WorldToViewportPoint(marker.transform.position);
            scene.OriginalA.Render(); scene.OriginalB.Render();
            if (uiEffect) Check(scene.OriginalA.GetComponent<MenuDepthAnchorsImageEffect724>().Draws > 0,
                "the native-style UI image effect is actually invoked by Camera.Render");
            Texture2D uiProbe = Read(scene.Ui, Path.Combine(output, "724-ui-viewport-probe.png"));
            Vector2 uiRaster = CyanCentre(uiProbe); Object.DestroyImmediate(uiProbe);
            Vector2 nativeUv = new Vector2((nativeRaster.x + .5f) / scene.ModelTarget.width, (nativeRaster.y + .5f) / scene.ModelTarget.height);
            Rect rawRect = raw.rectTransform.rect;
            Vector3 sampledPoint = raw.rectTransform.TransformPoint(new Vector3(rawRect.x + (nativeUv.x - raw.uvRect.x) / raw.uvRect.width * rawRect.width,
                rawRect.y + (nativeUv.y - raw.uvRect.y) / raw.uvRect.height * rawRect.height));
            Vector3 uiViewport = scene.OriginalA.WorldToViewportPoint(sampledPoint);
            Vector2 fullNative = new Vector2(nativeViewport.x * scene.ModelTarget.width - .5f, nativeViewport.y * scene.ModelTarget.height - .5f);
            Vector2 fullUi = new Vector2(uiViewport.x * scene.Ui.width - .5f, uiViewport.y * scene.Ui.height - .5f);
            Vector2 rectNative = new Vector2((scene.ModelCamera.rect.x + nativeViewport.x * scene.ModelCamera.rect.width) * scene.ModelTarget.width - .5f,
                (scene.ModelCamera.rect.y + nativeViewport.y * scene.ModelCamera.rect.height) * scene.ModelTarget.height - .5f);
            Vector2 rectUi = new Vector2((scene.OriginalA.rect.x + uiViewport.x * scene.OriginalA.rect.width) * scene.Ui.width - .5f,
                (scene.OriginalA.rect.y + uiViewport.y * scene.OriginalA.rect.height) * scene.Ui.height - .5f);
            File.WriteAllText(Path.Combine(output, "724-viewport-probes.txt"), "model viewport=" + nativeViewport
                + "; native raster=" + nativeRaster + "; native size=" + scene.ModelTarget.width + "," + scene.ModelTarget.height
                + "; UI raster=" + uiRaster + "; UI size=" + scene.Ui.width + "," + scene.Ui.height
                + "; sampled UI viewport=" + uiViewport.ToString("F6")
                + "; full model pixel=" + fullNative + "; rect model pixel=" + rectNative
                + "; full UI pixel=" + fullUi + "; rect UI pixel=" + rectUi + "\n");
            Check(Vector2.Distance(nativeRaster, rectNative) < .75f && Vector2.Distance(nativeRaster, fullNative) > 1.2f,
                "actual native HDR-disabled model render target retains its authored camera viewport");
            Check(Vector2.Distance(uiRaster, rectUi) < .75f && Vector2.Distance(uiRaster, fullUi) > 10f,
                "actual native HDR-disabled UI render target retains its authored camera viewport");
            int before = Time.frameCount; while (Time.frameCount == before) yield return null;
            Vector3 fixedPoint = VirtualLandmark(scene, raw, marker.transform.position);
            File.WriteAllText(Path.Combine(output, "724-anchor-calibration.txt"), "Fixed physical landmark=" + fixedPoint + "\n");
            Vector3 pane = WindowPoint(scene, raw.rectTransform.TransformPoint(raw.rectTransform.rect.center));
            Vector3 offsetToPane = new Vector3(Vector3.Dot(pane - scene.Screen.position, scene.Screen.right),
                Vector3.Dot(pane - scene.Screen.position, scene.Screen.up), 0);
            Vector3[] offsets = { offsetToPane + new Vector3(0, 0, -2), offsetToPane,
                offsetToPane + new Vector3(.23f, 0, 0), offsetToPane + new Vector3(0, .15f, 0),
                offsetToPane + new Vector3(0, 0, 1.6f), offsetToPane + new Vector3(.02f, -.01f, 1.6f),
                offsetToPane + new Vector3(.12f, .08f, -.18f),
                offsetToPane + new Vector3(0, 0, 4), offsetToPane + new Vector3(.20f, .13f, 3.5f),
                offsetToPane + new Vector3(-.23f, -.08f, 3.5f) };
            var sampled = new DesktopSamples725(raw); MenuDepthSources.Register(sampled);
            bool observing = false; var sizes = new List<int>();
            Camera.CameraCallback observer = camera =>
            {
                if (observing && camera == scene.ModelCamera && camera.targetTexture != scene.ModelTarget)
                    sizes.Add(camera.targetTexture.width);
            };
            Camera.onPreRender += observer;
            int farSize = 0;
            try
            {
            for (int i = 0; i < offsets.Length; i++)
            {
                before = Time.frameCount; while (Time.frameCount == before) yield return null;
                bool rear = offsets[i].z > 2f;
                scene.Eyes(offsets[i], rear);
                for (int eye = 0; eye < 2; eye++)
                {
                    Vector3 position = scene.Head.GetStereoViewMatrix(eye == 0 ? Camera.StereoscopicEye.Left : Camera.StereoscopicEye.Right)
                        .inverse.MultiplyPoint3x4(Vector3.zero);
                    scene.Head.transform.SetPositionAndRotation(position, scene.Screen.rotation * Quaternion.Euler(0, rear ? 180 : 0, 0));
                    MenuDepthAnchorsImageEffect724? effect = modelEffect ? scene.ModelCamera.GetComponent<MenuDepthAnchorsImageEffect724>()
                        : uiEffect ? scene.OriginalA.GetComponent<MenuDepthAnchorsImageEffect724>() : null;
                    int effectDraws = effect != null ? effect.Draws : 0;
                    int priorSizes = sizes.Count; observing = true;
                    try { scene.Render(eye != 0); } finally { observing = false; }
                    if (eye == 0)
                    {
                        Check(sizes.Count == priorSizes + 2 && sampled.Left.x > 0 && sampled.Left.y > 0,
                            "the calibrated original renders both sampled eye targets from a mapped physical pixel demand");
                        if (i == 0) farSize = sizes[priorSizes];
                        if (i == 4) Check(farSize < sizes[priorSizes] && sizes[priorSizes] <= 2048,
                            "the real original character eye targets grow from far to near displayed pixels within their bounded tier");
                        File.AppendAllText(Path.Combine(output, "725-native-screen-samples.csv"), i + "," + sampled.Left.x + ","
                            + sampled.Left.y + "," + sizes[priorSizes] + "," + sizes[priorSizes + 1] + "\n");
                    }
                    if (effect != null) Check(effect.Draws - effectDraws == (eye == 0 ? 2 : 0),
                        "each prepared eye retains the original image effect once without repeating it during the second final glass draw");
                    Texture2D image = Read(scene.HeadTarget, Path.Combine(output, "724-final-glass-" + i + "-" + eye + ".png"));
                    Vector2 observed = CyanCentre(image);
                    Vector3 viewport = scene.Head.WorldToViewportPoint(fixedPoint);
                    Vector2 expected = new(viewport.x * image.width - .5f, viewport.y * image.height - .5f);
                    float error = Vector2.Distance(observed, expected);
                    File.AppendAllText(Path.Combine(output, "724-final-anchor-errors.csv"), i + "," + eye + "," + observed.x + "," + observed.y
                        + "," + expected.x + "," + expected.y + "," + error + "\n");
                    Check(error < 3.5f,
                        "final physical glass keeps the independently calibrated native landmark fixed across lateral vertical depth and stereo head motion");
                    Object.DestroyImmediate(image);
                }
            }
            }
            finally { Camera.onPreRender -= observer; MenuDepthSources.Unregister(sampled); }
        }
        complete(_count);
    }

    private static Vector2 CyanCentre(Texture2D image)
    {
        Color32[] pixels = image.GetPixels32(); double x = 0, y = 0, total = 0;
        for (int at = 0; at < pixels.Length; at++)
        {
            Color32 p = pixels[at];
            if (p.r > 35 || p.g < 65 || p.b < 65 || Math.Abs(p.g - p.b) > 35) continue;
            float weight = Math.Min(p.g, p.b) - p.r; total += weight; x += at % image.width * weight; y += at / image.width * weight;
        }
        Check(total > 100, "native hierarchical landmark is visible through the actual final glass head draw");
        return new Vector2((float)(x / total), (float)(y / total));
    }

    private static Vector3 VirtualLandmark(Scene scene, RawImage image, Vector3 nativePoint)
    {
        // Independent inverse calibration from native frustum/UV and original UI
        // viewport to physical window. No tracked eye or production portal helper.
        var corners = new Vector3[4]; image.rectTransform.GetWorldCorners(corners);
        Vector3 bottomLeft = WindowPoint(scene, corners[0]), topLeft = WindowPoint(scene, corners[1]), bottomRight = WindowPoint(scene, corners[3]);
        Vector3 right = (bottomRight - bottomLeft).normalized, up = (topLeft - bottomLeft).normalized;
        Vector3 forward = Vector3.Cross(right, up).normalized;
        float width = Vector3.Distance(corners[3], corners[0]), height = Vector3.Distance(corners[1], corners[0]);
        // The native main actor ground is an explicit input. The source now
        // preserves one2.4-unit metric for every body; unrelated content and
        // conservative animated renderer envelopes never establish its frame.
        float xy = Math.Max(2.4f / (.82f * width), 2.4f / (.75f * height));
        Vector3 local = scene.ModelCamera.transform.InverseTransformPoint(nativePoint)
            - new Vector3(.1f, -.05f, 2.56f);
        float sx = Vector3.Distance(bottomRight, bottomLeft) / width, sy = Vector3.Distance(topLeft, bottomLeft) / height;
        return (topLeft + bottomRight) * .5f + right * (local.x / xy * sx) + up * (local.y / xy * sy)
            + forward * (local.z / xy * Mathf.Sqrt(sx * sy));
    }

    private static Vector3 WindowPoint(Scene scene, Vector3 nativeUiPoint)
    {
        Vector3 view = scene.OriginalA.WorldToViewportPoint(nativeUiPoint); Rect rect = scene.OriginalA.rect;
        return scene.Screen.TransformPoint(new Vector3(rect.x + view.x * rect.width - .5f, rect.y + view.y * rect.height - .5f));
    }

    private sealed class DesktopSamples725 : IMenuDepthSampledSource
    {
        public Graphic Graphic { get; }
        public bool IsReady => true;
        internal Vector2 Left;
        internal DesktopSamples725(Graphic graphic) => Graphic = graphic;
        public void BeforeCapture(MenuDepthEye eye) { if (!eye.Right) Left = eye.PixelFootprint; }
        public void AfterCapture() { }
    }
}
