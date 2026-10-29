using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    public static IEnumerator HeadCropMetric742(string output, Action<int> complete)
    {
        _count = 0;
        using (var scene = new Scene())
        {
            scene.Head.orthographic = false; scene.Head.fieldOfView = 70f;
            scene.Head.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            RawImage raw = null!;
            foreach (RawImage image in Object.FindObjectsOfType<RawImage>())
                if (ReferenceEquals(image.texture, scene.ModelTarget)) raw = image;
            Canvas.ForceUpdateCanvases();
            Check(MenuDepthScreenProjection.TryMap(scene.Screen, scene.OriginalA, raw.rectTransform, Vector3.zero,
                out Vector3 nativeEye, out Matrix4x4 nativeToPhysical), "original native preview has its independent physical eye map");
            var manager = scene.ModelCamera.GetComponent<Character3DDisplayManager>();
            Vector3 origin = scene.ModelCamera.transform.InverseTransformPoint(manager.Holder.GetChild(0).position);
            Check(MenuDepthCharacterProjection.TryBuild(scene.ModelCamera, manager.Holder, raw.rectTransform, raw.uvRect,
                nativeEye, origin, out var portal), "native actor portal exists before a storage-only crop");
            var head = new MenuDepthEye(false, nativeEye, scene.Head, worldToHeadClip:
                MenuDepthEye.HeadClip(false, Vector3.zero, scene.Head) * nativeToPhysical);
            var original = MenuDepthCharacterProjection.Head(portal, raw.rectTransform, head);
            var target = new RenderTexture(256, 256, 24);
            var fullTarget = new RenderTexture(1280, 960, 24);
            var graphics = new List<Graphic> { raw };
            var observation = new FrustumObservation729(raw);
            Matrix4x4 nativeView = scene.OriginalA.worldToCameraMatrix;
            Matrix4x4 nativeProjection = scene.OriginalA.projectionMatrix;
            RenderMode nativeMode = raw.canvas.rootCanvas.renderMode;
            try
            {
                var lease = new MenuDepthHeadRaster.FilterLease(new Vector2Int(640, 480), new RectInt(200, 100, 128, 128), target, scene.Glass);
                MenuDepthEye cropEye = lease.Eye(head);
                var cropped = MenuDepthCharacterProjection.Head(portal, raw.rectTransform, cropEye);
                Vector3 normal = new Vector3(.3f, .7f, .4f).normalized;
                Vector3 before = original.WorldToCamera.inverse.transpose.MultiplyVector(normal).normalized;
                Vector3 after = cropped.WorldToCamera.inverse.transpose.MultiplyVector(normal).normalized;
                float delta = 0f;
                for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
                    delta = Mathf.Max(delta, Mathf.Abs(original.WorldToCamera[row, column] - cropped.WorldToCamera[row, column]));
                File.WriteAllText(Path.Combine(output, "native-view-invariant.txt"), "viewDelta=" + delta
                    + ";normalAngle=" + Vector3.Angle(before, after) + ";nearRatio=" + cropped.Near / original.Near
                    + ";farRatio=" + cropped.Far / original.Far + "\n");
                Check(delta < .0001f && Vector3.Angle(before, after) < .001f
                    && Mathf.Abs(cropped.Near / original.Near - 1f) < .0001f
                    && Mathf.Abs(cropped.Far / original.Far - 1f) < .0001f,
                    "storage-only crop preserves original native view normals and physical clip metric");

                Matrix4x4 fullView, fullProjection;
                float fullNear, fullFar;
                using (var scope = new MenuDepthNativeUiScope(scene.OriginalA, raw.canvas.rootCanvas))
                {
                    scope.HeadCapture(fullTarget, head, graphics);
                    fullView = scene.OriginalA.worldToCameraMatrix;
                    fullProjection = scene.OriginalA.projectionMatrix;
                    fullNear = scene.OriginalA.nearClipPlane;
                    fullFar = scene.OriginalA.farClipPlane;
                }
                using (var scope = new MenuDepthNativeUiScope(scene.OriginalA, raw.canvas.rootCanvas))
                {
                    scope.HeadCapture(target, cropEye, graphics);
                    Matrix4x4 cropProjection = scene.OriginalA.projectionMatrix;
                    float fullPixelX = fullProjection.m00 * fullTarget.width;
                    float fullPixelY = fullProjection.m11 * fullTarget.height;
                    float cropPixelX = cropProjection.m00 * target.width;
                    float cropPixelY = cropProjection.m11 * target.height;
                    File.WriteAllText(Path.Combine(output, "native-ui-invariant.txt"),
                        "fullPixelX=" + fullPixelX + ";cropPixelX=" + cropPixelX
                        + ";fullPixelY=" + fullPixelY + ";cropPixelY=" + cropPixelY + "\n");
                    Check(Same(scene.OriginalA.worldToCameraMatrix, fullView)
                        && Mathf.Abs(cropPixelX / fullPixelX - 1f) < .0001f
                        && Mathf.Abs(cropPixelY / fullPixelY - 1f) < .0001f
                        && scene.OriginalA.nearClipPlane == fullNear && scene.OriginalA.farClipPlane == fullFar,
                        "native UI view and target pixel density remain invariant under a storage-only crop");
                }
                Check(Same(scene.OriginalA.worldToCameraMatrix, nativeView)
                    && Same(scene.OriginalA.projectionMatrix, nativeProjection)
                    && raw.canvas.rootCanvas.renderMode == nativeMode,
                    "both cropped native UI scopes restore the original camera and Canvas basis");

                MenuDepthSources.Register(observation);
                MenuDepthSources.BeforeCaptureWhere(head, g => ReferenceEquals(g, raw), g => cropEye,
                    g => new Vector2(317, 509));
                try
                {
                    Check(observation.Captures == 1 && observation.Seen.PixelFootprint == new Vector2(317, 509)
                        && Same(observation.Seen.WorldToHeadClip, cropEye.WorldToHeadClip)
                        && Same(observation.Seen.RasterProjection, cropEye.RasterProjection),
                        "actual sampled-source dispatch preserves cropped raster projection and native view metric");
                }
                finally { MenuDepthSources.AfterCapture(); }
            }
            finally
            {
                MenuDepthSources.Unregister(observation);
                target.Release(); Object.DestroyImmediate(target);
                fullTarget.Release(); Object.DestroyImmediate(fullTarget);
            }
        }
        complete(_count); yield break;
    }
}
