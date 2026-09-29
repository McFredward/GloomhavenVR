using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class BillboardRender
{
    // One shared material and thirteen tiny quads reproduce the actual hand constellation.
    // Away from world zero, CPU dynamic batching destroys an object-origin billboard.
    internal static int Run(Shader shader)
    {
        var material = new Material(shader) { color = Color.white };
        var cameraObject = new GameObject("translated native billboard camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(3f, 2f, 2f);
        camera.orthographic = true; camera.orthographicSize = .23f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        var rt = new RenderTexture(128, 128, 16); camera.targetTexture = rt;
        var pixels = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        var quads = new GameObject[13];
        for (int i = 0; i < quads.Length; i++)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quads[i] = quad;
            quad.transform.position = new Vector3(3f + (i % 4 - 1.5f) * .085f, 2f + (i / 4 - 1.5f) * .085f, 4f);
            quad.transform.localScale = Vector3.one * .065f;
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        int Read(string name)
        {
            camera.Render(); RenderTexture.active = rt;
            pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); pixels.Apply();
            int lit = 0; foreach (Color32 c in pixels.GetPixels32()) if (c.r > 100) lit++;
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-flameEvidence");
            if (at >= 0) File.WriteAllBytes(Path.Combine(args[at + 1], name + ".png"), pixels.EncodeToPNG());
            return lit;
        }
        try
        {
            material.SetFloat("_Billboard", 0f); int reference = Read("billboard-physical-reference");
            if (reference < 3000) throw new Exception("native billboard reference quads must be visible");
            material.SetFloat("_Billboard", 1f); int visible = Read("billboard-production");
            if (visible < reference * .85f) throw new Exception("shared native billboards retain individual translated origins");
            Shader negative = Shader.Find("GloomhavenVR/TownFlameBatchingNegative");
            if (negative == null) throw new Exception("compiled billboard batching negative shader exists");
            material.shader = negative; int collapsed = Read("billboard-batching-negative");
            if (collapsed > reference * .05f) throw new Exception("source-negative CPU batching must expose collapsed billboard origins");
            Debug.Log("Native billboard pixels: reference=" + reference + " production=" + visible + " source-negative=" + collapsed);
            return 4;
        }
        finally
        {
            foreach (GameObject quad in quads) Object.DestroyImmediate(quad);
            RenderTexture.active = null; camera.targetTexture = null;
            Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(rt);
            Object.DestroyImmediate(pixels); Object.DestroyImmediate(material);
        }
    }
}
