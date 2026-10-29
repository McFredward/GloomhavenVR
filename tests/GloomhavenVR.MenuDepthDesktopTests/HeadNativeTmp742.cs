using System;
using System.Collections;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MenuDepthDesktopProgram
{
    [Serializable] public sealed class TmpFloat742 { public string name = ""; public float value = 0; }
    [Serializable] public sealed class TmpColor742 { public string name = ""; public Color value = Color.white; }
    [Serializable] public sealed class TmpMaterial742
    {
        public string[] keywords = Array.Empty<string>(); public int renderQueue = 0;
        public TmpFloat742[] floats = Array.Empty<TmpFloat742>(); public TmpColor742[] colors = Array.Empty<TmpColor742>();
    }

    private sealed class NativeFont742 : IDisposable
    {
        internal readonly TMP_FontAsset Font;
        private readonly Texture2D _atlas;
        private readonly Material _material;

        internal NativeFont742(string output)
        {
            string inputs = Path.Combine(Path.GetDirectoryName(output)!, "native-tmp-inputs");
            FieldInfo settings = typeof(TMP_Settings).GetField("s_Instance", BindingFlags.Static | BindingFlags.NonPublic)!;
            if (settings.GetValue(null) == null) settings.SetValue(null, ScriptableObject.CreateInstance<TMP_Settings>());
            Font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            JsonUtility.FromJsonOverwrite(File.ReadAllText(Path.Combine(inputs, "font.json")), Font);
            _atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _atlas.LoadImage(File.ReadAllBytes(Path.Combine(inputs, "font-atlas.png")));
            Font.atlasTextures = new[] { _atlas }; Font.atlasPopulationMode = AtlasPopulationMode.Static;
            _material = new Material(Resources.Load<Shader>("OriginalShaders/NativeSDF")); _material.mainTexture = _atlas;
            var saved = JsonUtility.FromJson<TmpMaterial742>(File.ReadAllText(Path.Combine(inputs, "font-material.json")));
            _material.shaderKeywords = saved.keywords; _material.renderQueue = saved.renderQueue;
            foreach (var item in saved.floats) _material.SetFloat(item.name, item.value);
            foreach (var item in saved.colors) _material.SetColor(item.name, item.value);
            Font.material = _material; Font.ReadFontAssetDefinition();
        }

        internal TextMeshProUGUI Label(Transform parent, string name, string value, Vector2 size, Vector2 position, float fontSize)
        {
            var node = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            node.layer = parent.gameObject.layer; node.transform.SetParent(parent, false);
            var text = node.AddComponent<TextMeshProUGUI>(); text.font = Font; text.fontSharedMaterial = _material;
            text.text = value; text.fontSize = fontSize; text.color = Color.white; text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = size; text.rectTransform.anchoredPosition = position;
            text.ForceMeshUpdate(true, true);
            Check(text.textInfo.characterCount == value.Length && text.textInfo.meshInfo[0].vertexCount > 0,
                "original native static TMP font generates actual glyph geometry before its first head draw");
            return text;
        }

        public void Dispose()
        {
            Object.DestroyImmediate(Font); Object.DestroyImmediate(_material); Object.DestroyImmediate(_atlas);
        }
    }

    public static IEnumerator HeadNativeTmp742(string output, Action<int> complete)
        => HeadMotionCore730(output, complete, 640, 480, false, nativeTmp: true);

    private static void CheckTmpPixels742(Texture2D actual, Texture2D reference, GameObject original,
        Camera head, string output, int route, int pose, int eye)
    {
        Color32[] a = actual.GetPixels32(), b = reference.GetPixels32();
        foreach (TextMeshProUGUI text in original.GetComponentsInChildren<TextMeshProUGUI>())
        {
            foreach (TextMeshProUGUI source in Object.FindObjectsOfType<TextMeshProUGUI>())
                if (source.gameObject.layer == 5 && source.name == text.name)
                    File.AppendAllText(Path.Combine(output, "native-tmp-scale.csv"), route + "," + pose + "," + eye + ","
                        + text.name + ",nativeScale=" + source.transform.lossyScale.ToString("R") + ",referenceScale=" + text.transform.lossyScale.ToString("R")
                        + ",nativeUv=" + source.mesh.uv2[0].ToString("R") + ",referenceUv=" + text.mesh.uv2[0].ToString("R") + "\n");
            RectTransform rect = text.rectTransform;
            var plane = new Plane(rect.forward, rect.position);
            double sa = 0, sb = 0, aa = 0, bb = 0, ab = 0;
            int pixels = 0;
            for (int y = 0; y < actual.height; y++) for (int x = 0; x < actual.width; x++)
            {
                Ray ray = head.ViewportPointToRay(new Vector3((x + .5f) / actual.width, (y + .5f) / actual.height));
                if (!plane.Raycast(ray, out float distance) || !rect.rect.Contains(rect.InverseTransformPoint(ray.GetPoint(distance)))) continue;
                int index = y * actual.width + x;
                double value = a[index].r, expected = b[index].r * b[index].a / 255.0;
                sa += value; sb += expected; aa += value * value; bb += expected * expected; ab += value * expected;
                pixels++;
            }
            if (pixels < 10) continue; // an independently off-screen label supplies no pixels
            double correlation = (ab - sa * sb / pixels) / Math.Sqrt(Math.Max(1e-12,
                (aa - sa * sa / pixels) * (bb - sb * sb / pixels)));
            File.AppendAllText(Path.Combine(output, "native-tmp-pixels.csv"), route + "," + pose + "," + eye + ","
                + text.name + "," + pixels + "," + sb + "," + correlation + "\n");
            // This isolated SDF witness has the same exact2x area samples in
            // both paths. Allow8-bit/float roundoff, while detecting a changed
            // native font softness that the whole-window0.99 oracle can hide.
            Check(sb > 20 && correlation > .999,
                "actual final head native TMP glyphs retain independent original-font pixel detail");
        }
    }
}
