using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using Object = UnityEngine.Object;

// Actual Unity frames, original Camera.Render/effects, source leases, true-depth
// UI composition and both final raster paths execute production. Authored box
// geometry and tracked eye observations are explicit fixture inputs. The spatial
// oracle intersects final camera rays with that independently specified box.
public static class MenuDepthCharacterMotion729
{
    private static int _checks;
    private static void Check(bool value, string text) { _checks++; if (!value) throw new Exception(text); }
    private static readonly Vector3 Origin = new Vector3(.23f, -.676f, 3.036f);
    private const float Height = 2.257989f;
    private const int Frequency = 24;

    public static IEnumerator Motion729(string output)
    {
        _checks = 0; VRSession.IsRunning = true;
        using (var csv = new StreamWriter(Path.Combine(output, "continuous-motion.csv")))
        foreach (int path in new[] { 1, 2 })
        using (var scene = new MenuDepthCharacterCabinet726.Scene(output, path))
        {
            scene.SetActor(Origin, Height, false); scene.AddOffscreenAccessory(); scene.SetPattern(Frequency);
            if (path == 2) scene.TranslateNative(new Vector3(120f, -100f, 160f));
            var ids = new HashSet<int>();
            int warmedLeft = 0, warmedRight = 0, changes = 0;
            var summary = new Summary { path = path, frames = 52, nativeOriginalRasterOnly = path == 2, minimumCorrelation = 1, minimumContrast = 1 };
            csv.WriteLine("path,step,eye,pose,correlation,contrast,samples,leftID,rightID,width,height,cropWidth,cropHeight,foregroundWidth,foregroundHeight,poolBytes");
            for (int step = -4; step < 48; step++)
            {
                // Front/rear are valid exterior viewpoints. Near motion reaches
                // within4cm of the actual body face; X/Y and steep oblique views
                // continuously change the visible crop instead of jumping poses.
                float phase = step < 0 ? (step + 4) * Mathf.PI * .5f : step * Mathf.PI / 12f;
                bool rear = step >= 24;
                // This cabinet's desktop path deliberately retains the original
                // fixed raster; the separate Desktop suite executes the actual
                // expanded native carrier/coordinator. Keep its source-motion
                // rays within that native raster, while the physical SS path
                // also proves steep foreground outside the original window.
                float lateral = path == 2 ? .12f : .62f;
                Vector3 center = new Vector3(lateral * Mathf.Sin(phase), .10f * Mathf.Sin(phase * 2f),
                    (rear ? 1f : -1f) * (.16f + .07f * Mathf.Cos(phase)));
                yield return null;
                RenderTexture? beforeLeft = scene.Source.DepthTexture(false), beforeRight = scene.Source.DepthTexture(true);
                for (int eye = 0; eye < 2; eye++)
                {
                    Vector3 pose = center; pose.x += (eye == 0 ? -.032f : .032f) * (rear ? -1f : 1f);
                    Texture2D image = scene.Capture(pose, eye != 0, "motion-" + path + "-" + step + "-" + eye, true,
                        save: step == 0 || step == 12 || step == 24 || step == 36);
                    try
                    {
                        double correlation = Detail(image, scene, rear, out int count, out double contrast);
                        Check(correlation > .80 && contrast > .14,
                            "actual motion rasters retain the independently authored spatial detail");
                        RenderTexture? left = scene.Source.DepthTexture(false), right = scene.Source.DepthTexture(true);
                        Check(left != null && right != null && left.IsCreated() && right.IsCreated(),
                            "both actual eye targets remain created during continuous head motion");
                        ids.Add(left!.GetInstanceID()); ids.Add(right!.GetInstanceID());
                        Rect crop = (Rect)typeof(MenuDepthCharacterSource).GetField("_captureClip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene.Source)!;
                        Vector2 foreground = scene.Source.TryGetForegroundBounds(scene.LastEye, out Rect bounds)
                            ? new Vector2(bounds.width / scene.Graphic.rectTransform.rect.width, bounds.height / scene.Graphic.rectTransform.rect.height) : Vector2.zero;
                        summary.maximumCropWidth = Mathf.Max(summary.maximumCropWidth, crop.width);
                        summary.maximumCropHeight = Mathf.Max(summary.maximumCropHeight, crop.height);
                        summary.maximumForegroundWidth = Mathf.Max(summary.maximumForegroundWidth, foreground.x);
                        summary.maximumForegroundHeight = Mathf.Max(summary.maximumForegroundHeight, foreground.y);
                        summary.minimumCorrelation = Math.Min(summary.minimumCorrelation, correlation);
                        summary.minimumContrast = Math.Min(summary.minimumContrast, contrast);
                        long pool = (long)typeof(MenuDepthCharacterSource).GetField("_poolBytes", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                        summary.maximumPoolBytes = Math.Max(summary.maximumPoolBytes, pool);
                        csv.WriteLine(path + "," + step + "," + eye + ",\"" + pose + "\"," + correlation + "," + contrast + "," + count
                            + "," + left.GetInstanceID() + "," + right.GetInstanceID() + "," + left.width + "," + left.height
                            + "," + crop.width + "," + crop.height + "," + foreground.x + "," + foreground.y + "," + pool);
                        Check(pool > 0 && pool <=  96L * 1024 * 1024,
                            "continuous head motion remains inside the original shared96MiB reservation");
                        if (step >= 0 && eye == 1)
                        {
                            if (warmedLeft != 0 && (warmedLeft != left.GetInstanceID() || warmedRight != right.GetInstanceID())) changes++;
                            warmedLeft = left.GetInstanceID(); warmedRight = right.GetInstanceID();
                            Check(changes <= 2,
                                "continuous ordinary head motion preserves the warmed native eye target identities");
                        }
                        if (eye == 0) { beforeLeft = left; beforeRight = right; }
                        else Check(beforeLeft != null && beforeRight != null && beforeLeft.IsCreated() && beforeRight.IsCreated()
                            && ReferenceEquals(beforeLeft, left) && ReferenceEquals(beforeRight, right),
                            "the right capture never invalidates either already captured eye surface");
                    }
                    finally { Object.DestroyImmediate(image); }
                }
                csv.Flush();
            }
            Check(ids.Count <= 8, "the complete moving trajectory has a bounded actual GPU allocation count");
            // No crop-shrink delay is permitted after a near excursion. Compare
            // the immediate normal view against the same fixed spatial oracle.
            yield return null;
            Texture2D returned = scene.Capture(new Vector3(0, 0, -1.2f), false, "normal-return-" + path, true);
            try
            {
                double correlation = Detail(returned, scene, false, out _, out double contrast);
                summary.returnCorrelation = correlation; summary.returnContrast = contrast;
                Check(correlation > .80 && contrast > .14,
                    "normal view immediately recovers spatial detail after the near-plane trajectory");
            }
            finally { Object.DestroyImmediate(returned); }
            if (path == 1) scene.CheckNativeWindowGeometry();
            else Check(scene.Frame.rect.size == new Vector2(1200, 900)
                && scene.Frame.localScale == Vector3.one * .001f,
                "native screen captures retain the original physical window and hit geometry");
            summary.targetPairChanges = changes; summary.uniqueTargetIds = ids.Count;
            File.WriteAllText(Path.Combine(output, "motion-summary-" + path + ".json"), JsonUtility.ToJson(summary, true));
        }
        yield return _checks;
    }

    public static IEnumerator FarAccessory729(string output)
    {
        _checks = 0; VRSession.IsRunning = true;
        using (var scene = new MenuDepthCharacterCabinet726.Scene(output, 1))
        {
            // This physically distant accessory can really enter the grazing
            // head frustum. Its conservative union must survive, even when the
            // finite outer-window sample budget cannot retain native density.
            // Keep this explicit stress separate from normal model detail proof.
            scene.SetActor(Origin, Height, false); scene.AddOffscreenAccessory(80f);
            int changes = 0, lastLeft = 0; long maximumPool = 0; float maximumCrop = 0;
            foreach (float x in new[] { -.19f, -.50f, -.63f, -.50f, -.19f, .19f, .50f, .63f })
            {
                yield return null;
                for (int eye = 0; eye < 2; eye++)
                {
                    Texture2D image = scene.Capture(new Vector3(x + (eye == 0 ? -.032f : .032f), .05f, -.09f),
                        eye != 0, "far-accessory-" + x + "-" + eye, true, false);
                    Object.DestroyImmediate(image);
                    RenderTexture? left = scene.Source.DepthTexture(false), right = scene.Source.DepthTexture(true);
                    Check(left != null && right != null && left.IsCreated() && right.IsCreated(),
                        "physically visible distant accessories preserve real3D captures under the fixed source budget");
                    long pool = (long)typeof(MenuDepthCharacterSource).GetField("_poolBytes", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                    Rect crop = (Rect)typeof(MenuDepthCharacterSource).GetField("_captureClip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene.Source)!;
                    maximumPool = Math.Max(maximumPool, pool); maximumCrop = Mathf.Max(maximumCrop, crop.width);
                    Check(pool <= 96L * 1024 * 1024, "distant accessory stress cannot bypass the shared96MiB cap");
                    if (eye == 1 && lastLeft != 0 && lastLeft != left!.GetInstanceID()) changes++;
                    if (eye == 1) lastLeft = left!.GetInstanceID();
                }
            }
            Check(maximumCrop > 20f, "the distant accessory stress retains the actual large foreground extent");
            Check(changes <= 2, "distant foreground extent changes reuse bounded eye capacity");
            File.WriteAllText(Path.Combine(output, "far-accessory-budget.json"),
                "{\"maximumPoolBytes\":" + maximumPool + ",\"maximumCropWidth\":" + maximumCrop + ",\"pairChanges\":" + changes
                + ",\"outerNativeDensityClaimed\":false}\n");
        }
        yield return _checks;
    }

    [Serializable]
    private sealed class Summary
    {
        public int path, frames, targetPairChanges, uniqueTargetIds;
        public bool nativeOriginalRasterOnly;
        public long maximumPoolBytes;
        public float maximumCropWidth, maximumCropHeight, maximumForegroundWidth, maximumForegroundHeight;
        public double minimumCorrelation, minimumContrast, returnCorrelation, returnContrast;
    }

    private static double Detail(Texture2D pixels, MenuDepthCharacterCabinet726.Scene scene, bool rear, out int count, out double contrast)
    {
        Vector3 plane = scene.World(Origin + new Vector3(0, 0, rear ? .18f : -.18f), Origin);
        Vector3 normal = scene.Frame.forward;
        float units = Mathf.Max(2.4f / (1.2f * .82f), 2.4f / (.9f * .75f));
        Color32[] colours = pixels.GetPixels32();
        double a = 0, b = 0, aa = 0, bb = 0, ab = 0; count = 0;
        for (int y = 2; y < pixels.height - 2; y += 3) for (int x = 2; x < pixels.width - 2; x += 3)
        {
            Ray ray = scene.Head.ViewportPointToRay(new Vector3((x + .5f) / pixels.width, (y + .5f) / pixels.height));
            float denominator = Vector3.Dot(ray.direction, normal);
            if (Mathf.Abs(denominator) < 1e-7f) continue;
            float t = Vector3.Dot(plane - ray.origin, normal) / denominator;
            if (t <= 0) continue;
            Vector3 relative = ray.origin + ray.direction * t - scene.Frame.position;
            float nativeX = Vector3.Dot(relative, scene.Frame.right) * units;
            float nativeY = Vector3.Dot(relative, scene.Frame.up) * units + 1.2f;
            float uv = nativeY / Height;
            if (Mathf.Abs(nativeX) > .17f || uv < .08f || uv > .92f) continue;
            Color32 c = colours[y * pixels.width + x];
            double u = .5 + (rear ? -1 : 1) * nativeX / .45;
            double expected = .5 + .25 * Math.Sin(2 * Math.PI * Frequency * uv)
                + .25 * Math.Sin(2 * Math.PI * 12 * u), observed = c.r / 255.0;
            a += expected; b += observed; aa += expected * expected; bb += observed * observed; ab += expected * observed; count++;
        }
        Check(count > 40, "the independent final-camera rays intersect visible native body detail");
        contrast = Math.Sqrt(Math.Max(0, bb / count - b * b / (count * (double)count)));
        return (ab - a * b / count) / Math.Sqrt((aa - a * a / count) * (bb - b * b / count));
    }
}
