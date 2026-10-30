using System;
using System.Collections;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    private sealed class FrustumObservation729 : IMenuDepthSampledSource
    {
        public Graphic Graphic { get; }
        public bool IsReady => true;
        internal MenuDepthEye Seen;
        internal int Captures;
        internal FrustumObservation729(Graphic graphic) => Graphic = graphic;
        public void BeforeCapture(MenuDepthEye eye) { Seen = eye; Captures++; }
        public void AfterCapture() { }
    }

    public static IEnumerator HeadFrustum729(string output, Action<int> complete)
    {
        _count = 0;
        var native = new GameObject("native UI camera").AddComponent<Camera>();
        var head = new GameObject("physical viewer").AddComponent<Camera>();
        var screen = new GameObject("physical window").transform;
        var canvas = new GameObject("native canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var raw = new GameObject("native portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
        raw.transform.SetParent(canvas.transform, false);
        var observation = new FrustumObservation729(raw);
        try
        {
            native.enabled = head.enabled = false;
            native.orthographic = true; native.orthographicSize = 47f; native.aspect = 1.6f;
            native.transform.SetPositionAndRotation(new Vector3(40, -12, -55), Quaternion.Euler(12, 35, 4));
            native.rect = new Rect(.13f, .16f, .68f, .59f);
            head.orthographic = false; head.fieldOfView = 82f; head.aspect = 1.4f;
            raw.transform.SetPositionAndRotation(native.ViewportToWorldPoint(new Vector3(.46f, .58f, 100f)), native.transform.rotation);
            raw.rectTransform.sizeDelta = new Vector2(56, 89);
            screen.SetPositionAndRotation(new Vector3(.3f, .2f, 2.5f), Quaternion.Euler(9, 23, -4));
            screen.localScale = new Vector3(2.4f, 1.7f, 1f);
            Canvas.ForceUpdateCanvases();
            MenuDepthSources.Register(observation);
            for (int side = 0; side < 4; side++)
            {
                bool rear = side >= 2;
                Vector3 physicalEye = screen.TransformPoint(new Vector3((side & 1) == 0 ? -.08f : .08f, .1f, rear ? 1.6f : -1.2f));
                head.transform.SetPositionAndRotation(physicalEye, Quaternion.LookRotation(screen.position - physicalEye, screen.up));
                Check(MenuDepthScreenProjection.TryMap(screen, native, raw.rectTransform, physicalEye,
                    out Vector3 nativeEye, out Matrix4x4 nativeToPhysical), "native eye mapping admits front and rear views");
                Check(Vector3.Distance(nativeToPhysical.MultiplyPoint3x4(nativeEye), physicalEye) < .0003f,
                    "native frustum mapping recovers the actual physical viewer through anisotropic UI scale");
                Matrix4x4 physicalClip = head.projectionMatrix * head.worldToCameraMatrix;
                var mapped = new MenuDepthEye((side & 1) != 0, nativeEye, head, true,
                    worldToHeadClip: physicalClip * nativeToPhysical);
                foreach (Vector2 uv in new[] { new Vector2(.05f, .15f), new Vector2(.5f, .5f), new Vector2(1.2f, .85f) })
                {
                    Vector3 nativePoint = native.ViewportToWorldPoint(new Vector3(uv.x, uv.y, 100f));
                    Vector3 physicalPoint = screen.TransformPoint(new Vector3(native.rect.x + uv.x * native.rect.width - .5f,
                        native.rect.y + uv.y * native.rect.height - .5f));
                    Check(Vector3.Distance(nativeToPhysical.MultiplyPoint3x4(nativePoint), physicalPoint) < .0003f,
                        "native frustum mapping preserves original partial-viewport pixels on the physical glass");
                    Vector4 actual = mapped.WorldToHeadClip * new Vector4(nativePoint.x, nativePoint.y, nativePoint.z, 1f);
                    Vector4 expected = physicalClip * new Vector4(physicalPoint.x, physicalPoint.y, physicalPoint.z, 1f);
                    Check(Vector4.Distance(actual / actual.w, expected / expected.w) < .0005f,
                        "native frustum clipping uses the physical viewer rather than the native UI camera field of view");
                }
                int before = observation.Captures;
                MenuDepthSources.BeforeCaptureWhere(new MenuDepthEye(false, physicalEye, head),
                    g => ReferenceEquals(g, raw), g => mapped, g => new Vector2(317, 509));
                try
                {
                    Check(observation.Captures == before + 1 && observation.Seen.PixelFootprint == new Vector2(317, 509),
                        "the real native source dispatch receives its physical raster demand");
                    Check(observation.Seen.HasWorldToHeadClip && Same(observation.Seen.WorldToHeadClip, mapped.WorldToHeadClip),
                        "sampling-demand dispatch preserves the mapped physical frustum");
                }
                finally { MenuDepthSources.AfterCapture(); }
            }
        }
        finally
        {
            MenuDepthSources.Unregister(observation);
            foreach (GameObject go in new[] { native.gameObject, head.gameObject, screen.gameObject, canvas.gameObject })
                Object.DestroyImmediate(go);
        }
        complete(_count);
        yield break;
    }
}
