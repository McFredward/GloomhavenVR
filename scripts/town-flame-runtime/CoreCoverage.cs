using System;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class CoreCoverage
{
    internal static int Run(Shader shader)
    {
        float Difference(Shader candidate, float visibility, bool core)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, new Color(.9f, .6f, .2f, .6f)); texture.Apply();
            var material = new Material(candidate) { mainTexture = texture, color = new Color(.5f, .5f, .5f, .5f) };
            material.SetFloat("_FlameCore", core ? 1f : 0f); material.SetFloat("_TownVisibility", visibility);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            var root = new GameObject("Candle core coverage camera"); var camera = root.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -2); camera.orthographic = true; camera.orthographicSize = .6f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.allowHDR = false;
            var target = new RenderTexture(32, 32, 24); var pixels = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color Read(Color background)
            {
                camera.backgroundColor = background; camera.targetTexture = target; camera.Render();
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); pixels.Apply();
                return pixels.GetPixel(16, 16);
            }
            try
            {
                Color a = Read(Color.black), b = Read(new Color(.3f, .3f, .3f, 1));
                return Mathf.Abs(a.r-b.r) + Mathf.Abs(a.g-b.g) + Mathf.Abs(a.b-b.b);
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = null;
                Object.DestroyImmediate(root); Object.DestroyImmediate(quad); Object.DestroyImmediate(material);
                Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }
        if (Difference(shader, 1, true) > .025f) throw new Exception("lit scenery cannot show through candle core");
        if (Difference(shader, .4f, true) < .10f) throw new Exception("candle core still follows intermediate station fade");
        if (Difference(shader, 1, false) < .10f) throw new Exception("original halos remain additive");
        Shader negative = Shader.Find("GloomhavenVR/TownFlameTransparentNegative");
        if (negative == null || Difference(negative, 1, true) < .10f) throw new Exception("old translucent candle core must fail rendered coverage");
        return 4;
    }
}
