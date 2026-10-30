using System;
using System.Collections;
using System.IO;
using System.Linq;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    private sealed class PreflightSource736 : IMenuDepthSource
    {
        public Graphic Graphic { get; }
        public bool IsReady => Graphic != null && Graphic.isActiveAndEnabled;
        internal int Prepared;
        internal PreflightSource736(Graphic graphic) { Graphic = graphic; }
        public void BeforeCapture(MenuDepthEye eye) { Prepared++; }
        public void AfterCapture() { }
    }

    private sealed partial class Scene
    {
        internal Camera EmptyUiCamera736()
        {
            Camera camera = CreateCamera("original UI camera awaiting its Canvas", 7, 2);
            camera.orthographic = true; camera.orthographicSize = 5;
            camera.transform.position = new Vector3(80, 20, 100);
            camera.nearClipPlane = .01f; camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.Nothing;
            return camera;
        }
        internal Graphic SupplyCanvas736(Camera camera)
        {
            RectTransform canvas = CreateCanvas(camera, 7, 12);
            return Image(canvas, 7, Vector2.zero, new Vector2(80, 50), Color.yellow);
        }
    }

    public static IEnumerator HeadPreflight736(string output, Action<int> complete)
    {
        _count = 0; DesktopEyeInput.Available = true;
        using (var scene = new Scene())
        {
            Camera empty = scene.EmptyUiCamera736();
            var source = new PreflightSource736(scene.SpatialGraphic);
            MenuDepthSources.Register(source);
            PreflightSource736? arrived = null;
            try
            {
                scene.Head.SetStereoViewMatrix(Camera.StereoscopicEye.Left,
                    Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.Translate(new Vector3(.03f, 0, 0)));
                scene.Head.SetStereoViewMatrix(Camera.StereoscopicEye.Right,
                    Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.Translate(new Vector3(-.03f, 0, 0)));
                scene.Flat.Begin();
                Check(scene.Flat.Captured == 3, "the original empty UI camera remains in the captured native stack");
                Vector3 pose = scene.Screen.position, scale = scene.Screen.localScale;
                for (int frame = 0; frame < 4; frame++)
                {
                    yield return null;
                    scene.Head.Render();
                    Check(source.Prepared == 0 && scene.Flat.Right == null && ReferenceEquals(scene.Flat.Left, scene.Ui),
                        "an unmapped native UI stack cannot admit head targets or prepare a partial spatial frame");
                    Check(empty.targetTexture == scene.Ui && empty.cullingMask == (1 << 7)
                        && scene.Screen.position == pose && scene.Screen.localScale == scale,
                        "mapping refusal preserves original camera ownership and fixed window input geometry");
                    Texture2D image = Read(scene.Ui, Path.Combine(output, "native-fallback-" + frame + ".png"));
                    Color32[] pixels = image.GetPixels32(); Object.DestroyImmediate(image);
                    Check(pixels.Any(pixel => pixel.r > 80 || pixel.g > 80 || pixel.b > 80),
                        "the original mono UI continues drawing during an unsupported mapping");
                }
                scene.Button.onClick.Invoke();
                Check(scene.Clicks == 1, "native input callbacks remain available while mapping is unavailable");
                arrived = new PreflightSource736(scene.SupplyCanvas736(empty));
                MenuDepthSources.Register(arrived); Canvas.ForceUpdateCanvases();
                yield return null;
                scene.Head.Render();
                Check(scene.Flat.Right != null && source.Prepared > 0 && arrived.Prepared > 0,
                    "a newly mapped original Canvas resumes both eye captures on its first ready head frame");
                File.WriteAllText(Path.Combine(output, "preflight.txt"),
                    "Four invalid-stack head frames: no extra eye target or partial source preparation; native mono/input preserved. "
                    + "Original camera retained; real Canvas arrival resumes stereo immediately.\n");
            }
            finally
            {
                MenuDepthSources.Unregister(source);
                if (arrived != null) MenuDepthSources.Unregister(arrived);
                if (empty != null) Object.DestroyImmediate(empty.gameObject);
            }
        }
        complete(_count);
    }
}
