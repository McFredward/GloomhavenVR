using System;
using System.Collections;
using System.IO;
using System.Linq;
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
            marker.transform.localScale = Vector3.one * .13f; marker.GetComponent<MeshRenderer>().sharedMaterial = material;
            RawImage raw = Object.FindObjectsOfType<RawImage>().Single(image => ReferenceEquals(image.texture, scene.ModelTarget));
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
            Vector3 fixedPoint = VirtualLandmark(scene, raw, holder, marker.transform.position);
            File.WriteAllText(Path.Combine(output, "724-anchor-calibration.txt"), "Fixed physical landmark=" + fixedPoint + "\n");
            Vector3[] offsets = { Vector3.zero, new Vector3(.23f, 0, 0), new Vector3(-.19f, 0, 0),
                new Vector3(0, .15f, 0), new Vector3(0, -.12f, 0), new Vector3(0, 0, .27f), new Vector3(.12f, .08f, -.18f) };
            for (int i = 0; i < offsets.Length; i++)
            {
                before = Time.frameCount; while (Time.frameCount == before) yield return null;
                scene.Eyes(offsets[i]);
                for (int eye = 0; eye < 2; eye++)
                {
                    Vector3 position = scene.Head.GetStereoViewMatrix(eye == 0 ? Camera.StereoscopicEye.Left : Camera.StereoscopicEye.Right)
                        .inverse.MultiplyPoint3x4(Vector3.zero);
                    scene.Head.transform.SetPositionAndRotation(position, scene.Screen.rotation);
                    MenuDepthAnchorsImageEffect724? effect = modelEffect ? scene.ModelCamera.GetComponent<MenuDepthAnchorsImageEffect724>()
                        : uiEffect ? scene.OriginalA.GetComponent<MenuDepthAnchorsImageEffect724>() : null;
                    int effectDraws = effect != null ? effect.Draws : 0;
                    scene.Render(eye != 0);
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

    private static Vector3 VirtualLandmark(Scene scene, RawImage image, Transform holder, Vector3 nativePoint)
    {
        // Independent inverse calibration from native frustum/UV and original UI
        // viewport to physical window. No tracked eye or production portal helper.
        Camera model = scene.ModelCamera; Matrix4x4 nativeProjection = model.projectionMatrix;
        float depth = Vector3.Dot(holder.position - model.transform.position, model.transform.forward) * .85f;
        float width = 2 * depth / nativeProjection.m00, height = 2 * depth / nativeProjection.m11;
        // The HDR-disabled camera contract is supplied from the actual native
        // camera census. Raster probes above establish its retained viewport;
        // HDR-enabled Unity intermediates are a different, unclaimed path.
        Rect viewport = model.rect, uv = image.uvRect;
        Vector3 nativeCentre = model.transform.position + model.transform.forward * depth
            + model.transform.right * (nativeProjection.m02 * depth / nativeProjection.m00)
            + model.transform.up * (nativeProjection.m12 * depth / nativeProjection.m11);
        Vector3 cropCentre = nativeCentre + model.transform.right * (((uv.center.x - viewport.x) / viewport.width - .5f) * width)
            + model.transform.up * (((uv.center.y - viewport.y) / viewport.height - .5f) * height);
        var corners = new Vector3[4]; image.rectTransform.GetWorldCorners(corners);
        Vector3 bottomLeft = WindowPoint(scene, corners[0]), topLeft = WindowPoint(scene, corners[1]), bottomRight = WindowPoint(scene, corners[3]);
        Vector3 right = (bottomRight - bottomLeft).normalized, up = (topLeft - bottomLeft).normalized;
        Vector3 forward = Vector3.Cross(right, up).normalized;
        float sx = Vector3.Distance(bottomRight, bottomLeft) / (width * uv.width / viewport.width);
        float sy = Vector3.Distance(topLeft, bottomLeft) / (height * uv.height / viewport.height);
        Vector3 offset = nativePoint - cropCentre;
        return (topLeft + bottomRight) * .5f + right * (Vector3.Dot(offset, model.transform.right) * sx)
            + up * (Vector3.Dot(offset, model.transform.up) * sy) + forward * (Vector3.Dot(offset, model.transform.forward) * Mathf.Sqrt(sx * sy));
    }

    private static Vector3 WindowPoint(Scene scene, Vector3 nativeUiPoint)
    {
        Vector3 view = scene.OriginalA.WorldToViewportPoint(nativeUiPoint); Rect rect = scene.OriginalA.rect;
        return scene.Screen.TransformPoint(new Vector3(rect.x + view.x * rect.width - .5f, rect.y + view.y * rect.height - .5f));
    }
}
