using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;

public static partial class DiagnosticProgram
{
    private static void QualifyPanelCopies()
    {
        RenderTexture priorTarget = RenderTexture.active; bool priorColor = GL.sRGBWrite;
        bool stereo = Shader.IsKeywordEnabled("STEREO_INSTANCING_ON");
        const int width = 96, height = 80;
        RenderTexture Target(bool mips, int samples)
        {
            var value = Keep(new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
            value.antiAliasing = samples; value.useMipMap = mips; value.autoGenerateMips = false; value.filterMode = FilterMode.Point;
            Check(value.Create(), "Actual panel fixture target is allocated"); return value;
        }
        Color32[] ReadTarget(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            var pixels = Keep(new Texture2D(width, height, TextureFormat.RGBA32, false, true));
            try
            {
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                return pixels.GetPixels32();
            }
            finally { RenderTexture.active = previous; }
        }
        void Equal(Color32[] actual, Color32[] expected, string reason)
        {
            Check(actual.Length == expected.Length, reason + " dimensions");
            int alphaPixels = 0;
            for (int i = 0; i < actual.Length; i++)
            {
                Color32 a = actual[i], b = expected[i];
                if (a.a > 0 && a.a < 255) alphaPixels++;
                Check(Math.Abs(a.r - b.r) <= 1 && Math.Abs(a.g - b.g) <= 1 && Math.Abs(a.b - b.b) <= 1 && Math.Abs(a.a - b.a) <= 1, reason);
            }
            Check(alphaPixels > 100, reason + " measures real premultiplied partial-alpha pixels");
        }
        try
        {
            Shader.DisableKeyword("STEREO_INSTANCING_ON");
            var source = Target(false, 4); var resolved = Target(true, 1); var reference = Target(false, 1);
            Check(source.antiAliasing == 4, "Native backend actually supplies requested MSAA4");
            var camera = Keep(new GameObject("panel-camera")).AddComponent<Camera>(); camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear; camera.orthographic = true;
            camera.orthographicSize = 1; camera.cullingMask = 1 << 30; camera.targetTexture = source;
            var face = Keep(new GameObject("premultiplied-panel", typeof(MeshFilter), typeof(MeshRenderer))); face.layer = 30;
            var mesh = Keep(new Mesh()); mesh.vertices = new[] { new Vector3(-1,-1,2), new Vector3(-1,1,2), new Vector3(1,1,2), new Vector3(1,-1,2) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white }; mesh.triangles = new[] { 0,1,2,0,2,3 };
            face.GetComponent<MeshFilter>().sharedMesh = mesh;
            var texture = Keep(new Texture2D(8, 8, TextureFormat.RGBA32, false, true)); texture.filterMode = FilterMode.Point;
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                float alpha = (x + y + 1) / 16f; texture.SetPixel(x, y, new Color((x + 1) / 8f * alpha, (y + 1) / 8f * alpha, .2f * alpha, alpha));
            }
            texture.Apply();
            var material = Keep(new Material(Resources.Load<Shader>("PrimitiveOverlay"))); material.mainTexture = texture;
            material.SetInt("_SrcBlend", 1); material.SetInt("_DstBlend", 10); material.SetInt("_ZWrite", 0);
            face.GetComponent<Renderer>().sharedMaterial = material;
            camera.Render(); Graphics.Blit(source, reference);
            Color32[] expected = ReadTarget(reference);
            var entry = new ActualPanelCopies.Entry { Rt = source, MipRt = resolved, Moving = true };
            Shader.EnableKeyword("STEREO_INSTANCING_ON");
            RenderTexture.active = priorTarget; GL.sRGBWrite = priorColor;
            ActualPanelCopies.Resolve(entry);
            Check(RenderTexture.active == priorTarget && GL.sRGBWrite == priorColor, "Panel MSAA resolve retains previous target and color state");
            Check(entry.MotionResolves == 1 && entry.StillResolves == 0 && entry.ResolveSizeMismatch == 0
                && entry.MipCount > 1 && Mathf.Abs(resolved.mipMapBias + .25f) < .001f, "Actual panel resolve preserves mip generation, LOD and observed cadence counts");
            Equal(ReadTarget(resolved), expected, "Actual Linear MSAA panel resolve preserves full image RGBA and alpha under stereo keyword");
            var carried = Target(true, 1); var replacement = new ActualPanelCopies.Entry { Rt = source, MipRt = carried };
            RenderTexture.active = priorTarget; GL.sRGBWrite = priorColor;
            Check(ActualPanelCopies.Carry(replacement, resolved), "Actual target replacement carries its previous finished image");
            Check(RenderTexture.active == priorTarget && GL.sRGBWrite == priorColor, "Panel carry retains previous target and color state");
            Equal(ReadTarget(carried), expected, "Actual panel carry preserves full image RGBA and alpha under stereo keyword");
            Check(!ActualPanelCopies.Carry(replacement, carried) && !ActualPanelCopies.Carry(replacement, null), "Same/null panel target stays a no-copy path");
            ActualPanelCopies.Resolve(entry, true);
            Check(entry.MotionResolves == 1 && entry.StillResolves == 0, "Priming never charges normal capture counters");
            entry.Moving = false; ActualPanelCopies.Resolve(entry); Check(entry.StillResolves == 1, "Still capture keeps its original counter");
            Shader.DisableKeyword("STEREO_INSTANCING_ON"); camera.targetTexture = reference; camera.Render();
            Equal(ReadTarget(reference), expected, "Following ordinary camera retains its original full viewport and RGBA");
            camera.targetTexture = source; face.SetActive(false);
        }
        finally
        {
            if (stereo) Shader.EnableKeyword("STEREO_INSTANCING_ON"); else Shader.DisableKeyword("STEREO_INSTANCING_ON");
            RenderTexture.active = priorTarget; GL.sRGBWrite = priorColor;
        }
    }
}
