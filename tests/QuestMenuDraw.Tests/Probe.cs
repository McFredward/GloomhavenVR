using System;
using System.IO;
using System.Reflection;
using GloomhavenVR.WorldUI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class QuestMenuDrawProbe
{
    [Serializable] sealed class Report
    {
        public string unity, graphics;
        public int comparisons, stateComparisons, defectControls;
        public bool hardwareVerified;
    }

    public static void Run()
    {
        var report = new Report { unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString() };
        var camera = new GameObject("actual-menu-probe-camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = new Vector3(.17f, -.11f, -.4f);
        camera.transform.rotation = Quaternion.Euler(3, 7, -5);
        camera.nearClipPlane = .05f; camera.farClipPlane = 20; camera.fieldOfView = 83;
        camera.aspect = 48f / 32; camera.allowHDR = false; camera.allowMSAA = false;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.renderingPath = RenderingPath.Forward;
        var expected = new RenderTexture(48, 32, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        expected.Create(); camera.targetTexture = expected;
        var sentinel = new RenderTexture(7, 9, 0); sentinel.Create();
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.transform.position = new Vector3(-.23f, -.08f, 2.1f);
        quad.transform.rotation = Quaternion.Euler(9, -12, 8);
        quad.transform.localScale = new Vector3(1.8f, .9f, 1);
        var material = new Material(Resources.Load<Shader>("Overlay"));
        material.SetFloat("_VertexColor", 0); material.SetFloat("_UseAlphaMask", 0);
        material.SetInt("_Cull", (int)CullMode.Off); material.SetInt("_ZWrite", 0);
        material.SetInt("_ZTest", (int)CompareFunction.Always);
        material.SetInt("_SrcBlend", (int)BlendMode.One);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        var renderer = quad.GetComponent<Renderer>(); renderer.sharedMaterial = material;
        var originalModel = quad.transform.localToWorldMatrix;
        var originalMesh = quad.GetComponent<MeshFilter>().sharedMesh;
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_Color", new Color(.7f, .6f, .4f, .8f));
        var foreground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        foreground.transform.position = new Vector3(0, 0, .8f);
        foreground.transform.localScale = new Vector3(.2f, .3f, .15f);
        var foregroundMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.magenta };
        foreground.GetComponent<Renderer>().sharedMaterial = foregroundMaterial;
        foreground.SetActive(false);
        var pattern = new Texture2D(8, 4, TextureFormat.RGBA32, false, true);
        var data = new Color32[32];
        for (int y = 0; y < 4; y++) for (int x = 0; x < 8; x++)
            data[y * 8 + x] = new Color32((byte)(30 + x * 20), (byte)(20 + y * 40), 70, (byte)(x < 4 ? 96 : 255));
        pattern.SetPixels32(data); pattern.Apply(); material.mainTexture = pattern;
        var method = typeof(FlatScreen).GetMethod("RenderQuestConsumerProbe", BindingFlags.Static | BindingFlags.NonPublic);
        RenderTexture Draw(Matrix4x4 view, Matrix4x4 projection)
            => (RenderTexture)method.Invoke(null, new object[] { renderer, material, view, projection });
        Color32[] Read(RenderTexture target)
        {
            var previous = RenderTexture.active;
            var cpu = new Texture2D(48, 32, TextureFormat.RGBA32, false, true);
            try { RenderTexture.active = target; cpu.ReadPixels(new Rect(0, 0, 48, 32), 0, 0); cpu.Apply(); return cpu.GetPixels32(); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(cpu); }
        }
        int Difference(Color32[] a, Color32[] b)
        {
            int max = 0;
            for (int index = 0; index < a.Length; index++)
            {
                max = Math.Max(max, Math.Abs(a[index].r - b[index].r));
                max = Math.Max(max, Math.Abs(a[index].g - b[index].g));
                max = Math.Max(max, Math.Abs(a[index].b - b[index].b));
                max = Math.Max(max, Math.Abs(a[index].a - b[index].a));
            }
            return max;
        }
        void Require(bool condition, string detail) { if (!condition) throw new Exception(detail); }
        try
        {
            foreach (bool glass in new[] { false, true })
            foreach (Vector3 pose in new[] { Vector3.zero, new Vector3(-5, 11, 17), new Vector3(8, -7, -9) })
            {
                material.SetInt("_ZTest", (int)(glass ? CompareFunction.LessEqual : CompareFunction.Always));
                renderer.SetPropertyBlock(glass ? properties : null);
                camera.transform.rotation = Quaternion.Euler(pose);
                camera.Render(); var before = Read(expected);
                int present = 0; foreach (var pixel in before) if (pixel.r > 2 || pixel.g > 2 || pixel.b > 2) present++;
                Require(present > 20, "Camera reference must visibly draw the native colour-less primitive");
                RenderTexture.active = sentinel;
                var actual = Draw(camera.worldToCameraMatrix, camera.projectionMatrix);
                Require(RenderTexture.active == sentinel, "Probe restores the exact previously bound render target");
                Require(actual.width == 48 && actual.height == 32, "Probe retains its bounded dimensions");
                var pixels = Read(actual);
                int difference = Difference(before, pixels);
                if (difference > 1)
                {
                    string evidence = Path.GetFullPath(Path.Combine(Application.dataPath, "../ProbeOutput"));
                    Directory.CreateDirectory(evidence);
                    foreach (var pair in new[] { Tuple.Create("expected", before), Tuple.Create("actual", pixels) })
                    {
                        var image = new Texture2D(48, 32, TextureFormat.RGBA32, false, true);
                        image.SetPixels32(pair.Item2); image.Apply();
                        File.WriteAllBytes(Path.Combine(evidence, pair.Item1 + ".png"), image.EncodeToPNG());
                        Object.DestroyImmediate(image);
                    }
                }
                Require(difference <= 1, "Actual mesh/material/model/view/projection RGBA differs from a camera draw by " + difference
                    + " glass=" + glass + " pose=" + pose);
                actual.Release(); Object.DestroyImmediate(actual);
                camera.Render();
                Require(Difference(before, Read(expected)) == 0, "A following real camera draw is unaffected by diagnostic matrix/target changes");
                report.comparisons++;
            }
            foreach (Vector3 pose in new[] { Vector3.zero, new Vector3(8, -7, -9) })
            {
                camera.transform.rotation = Quaternion.Euler(pose);
                foreground.SetActive(true);
                camera.Render(); var before = Read(expected);
                var consumer = Draw(camera.worldToCameraMatrix, camera.projectionMatrix);
                consumer.Release(); Object.DestroyImmediate(consumer);
                camera.Render();
                Require(Difference(before, Read(expected)) == 0,
                    "Following ordinary foreground/hand-like geometry pixels remain exact after a diagnostic render");
                report.stateComparisons++;
            }
            foreground.SetActive(false);
            var reference = Draw(camera.worldToCameraMatrix, camera.projectionMatrix);
            var referencePixels = Read(reference); reference.Release(); Object.DestroyImmediate(reference);
            material.mainTexture = Texture2D.blackTexture;
            var blank = Draw(camera.worldToCameraMatrix, camera.projectionMatrix);
            Require(Difference(referencePixels, Read(blank)) > 20, "Control detects a black consumer binding");
            blank.Release(); Object.DestroyImmediate(blank); material.mainTexture = pattern; report.defectControls++;
            var displaced = Draw(Matrix4x4.Translate(new Vector3(100, 0, 0)) * camera.worldToCameraMatrix, camera.projectionMatrix);
            Require(Difference(referencePixels, Read(displaced)) > 20, "Control detects incorrect view state/offscreen geometry");
            displaced.Release(); Object.DestroyImmediate(displaced); report.defectControls++;
            Require(originalModel == quad.transform.localToWorldMatrix && material.mainTexture == pattern
                && quad.GetComponent<MeshFilter>().sharedMesh == originalMesh,
                "Consumer evidence leaves the actual scene mesh, transform and texture unchanged");
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../ProbeOutput"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "results.json"), JsonUtility.ToJson(report, true) + "\n");
            Debug.Log("PASS actual menu consumer draw: comparisons=" + report.comparisons + " controls=" + report.defectControls);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(quad); Object.DestroyImmediate(pattern); Object.DestroyImmediate(material);
            Object.DestroyImmediate(foreground); Object.DestroyImmediate(foregroundMaterial);
            expected.Release(); Object.DestroyImmediate(expected); sentinel.Release(); Object.DestroyImmediate(sentinel);
        }
    }
}
