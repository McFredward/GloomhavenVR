using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Real native GPU characterization of the laser hit marker. This is deliberately
// independent of OpenXR: it separates ordinary Sprite/MeshRenderer semantics from
// tracked eye projection, and does not establish a Quest headset outcome.
public static class QuestPointerRenderProbe
{
    [Serializable] sealed class Row
    {
        public string name, shader, mesh;
        public int vertices, vertexStride, pixels, minX, maxX, minY, maxY;
        public bool expectedBounded, bounded;
        public string expectedPixels;
        public Vector3 bounds;
        public Vector4 flip, rendererColor;
        public Color center;
        [NonSerialized] public Color32[] pixelData;
    }
    [Serializable] sealed class Report
    {
        public string unity, graphics, colorSpace;
        public bool hardwareVerified = false;
        public List<Row> cases = new List<Row>();
    }

    public static void Run()
    {
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../ProbeOutput"));
        Directory.CreateDirectory(output);
        var colorSpace = QuestPointerProbeConfig.Linear ? ColorSpace.Linear : ColorSpace.Gamma;
        PlayerSettings.colorSpace = colorSpace;
        if (QualitySettings.activeColorSpace != colorSpace)
            throw new Exception("The pointer fixture must use its requested working color space");
        var report = new Report { unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString(),
            colorSpace = QualitySettings.activeColorSpace.ToString() };
        var target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        target.Create();
        var camera = new GameObject("pointer-native-gpu-camera").AddComponent<Camera>();
        camera.enabled = false; camera.targetTexture = target;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.fieldOfView = 60; camera.nearClipPlane = .01f; camera.farClipPlane = 10;
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(sphere.GetComponent<Collider>());
        var filter = sphere.GetComponent<MeshFilter>();
        var renderer = sphere.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        sphere.transform.position = new Vector3(.1f, .08f, 1.5f);
        sphere.transform.rotation = Quaternion.Euler(35, 55, 40);
        sphere.transform.localScale = Vector3.one * .02f;
        var native = filter.sharedMesh;
        var explicitSphere = Sphere();
        var factory = new GloomhavenVR.Hands.Interact.PointerProductionMaterialFactory();
        var sprite = factory.Build(false);
        var owned = factory.Build(true);
        var occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
        occluder.transform.position = new Vector3(.1f, .08f, 1);
        occluder.transform.localScale = Vector3.one * .15f;
        var occluderMaterial = new Material(Shader.Find("Unlit/Color"));
        occluderMaterial.color = Color.black;
        occluder.GetComponent<Renderer>().sharedMaterial = occluderMaterial;
        occluder.SetActive(false);
        var line = new GameObject("native-line-color-contract").AddComponent<LineRenderer>();
        line.useWorldSpace = true; line.positionCount = 2; line.widthMultiplier = .004f;
        line.SetPositions(new[] { new Vector3(-.2f, 0, 1.5f), new Vector3(.2f, .1f, 1.5f) });
        line.startColor = new Color(.45f, .8f, 1, .35f);
        line.endColor = new Color(.45f, .8f, 1, .05f);
        line.gameObject.SetActive(false);
        try
        {
            void Require(bool condition, string message)
            { if (!condition) throw new Exception(message); }
            Require(sprite.shader == Shader.Find("Sprites/Default"), "Desktop keeps its original native sprite material");
            Require(owned.shader.name == "GloomhavenVR/Overlay", "Quest resolves the already owned ordinary mesh shader");
            Require(owned.renderQueue == 4600 && sprite.renderQueue == 4600, "Both platform paths retain pointer render queue");
            Require(!owned.enableInstancing && owned.mainTexture == Texture2D.whiteTexture
                && owned.GetFloat("_SrcBlend") == (float)BlendMode.One
                && owned.GetFloat("_DstBlend") == (float)BlendMode.OneMinusSrcAlpha
                && owned.GetFloat("_ZTest") == (float)CompareFunction.LessEqual
                && owned.GetFloat("_ZWrite") == 0 && owned.GetFloat("_Cull") == (float)CullMode.Off,
                "Quest pins native Sprite premultiplied blend/depth policy on its plain mesh material");
            Row Capture(string name, Mesh mesh, Material material, string expectedPixels = "small", bool quest = false, bool corruptBlock = false)
            {
                filter.sharedMesh = mesh; renderer.sharedMaterial = material;
                if (line.gameObject.activeSelf)
                {
                    line.sharedMaterial = material;
                    line.startColor = factory.VertexColor(material, new Color(.45f, .8f, 1, .35f), quest);
                    line.endColor = factory.VertexColor(material, new Color(.45f, .8f, 1, .05f), quest);
                }
                renderer.SetPropertyBlock(null); line.SetPropertyBlock(null);
                if (corruptBlock)
                {
                    var corrupt = new MaterialPropertyBlock();
                    corrupt.SetVector("_Flip", new Vector4(100, 100, 1, 1));
                    corrupt.SetColor("_RendererColor", Color.red);
                    corrupt.SetFloat("_PointerUnrelatedFixtureValue", 7);
                    renderer.SetPropertyBlock(corrupt); line.SetPropertyBlock(corrupt);
                }
                factory.Apply(renderer, quest); factory.Apply(line, quest);
                if (!quest) Require(renderer.HasPropertyBlock() == corruptBlock && line.HasPropertyBlock() == corruptBlock,
                    "Desktop does not add or replace a renderer property block");
                if (quest)
                {
                    var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                    Require(block.GetVector("_Flip") == Vector4.one && block.GetColor("_RendererColor") == Color.white,
                        "Quest final renderer block overrides sprite-only inputs");
                    if (corruptBlock) Require(block.GetFloat("_PointerUnrelatedFixtureValue") == 7,
                        "Quest sprite repair retains unrelated renderer block state");
                }
                camera.Render();
                var previous = RenderTexture.active;
                var image = new Texture2D(512, 512, TextureFormat.RGBA32, false, true);
                try
                {
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); image.Apply();
                    var row = new Row { name = name, shader = material.shader.name, mesh = mesh.name,
                        vertices = mesh.vertexCount, vertexStride = mesh.GetVertexBufferStride(0), bounds = renderer.bounds.size,
                        minX = 512, minY = 512, maxX = -1, maxY = -1,
                        expectedPixels = expectedPixels, expectedBounded = expectedPixels == "small",
                        flip = material.HasProperty("_Flip") ? material.GetVector("_Flip") : Vector4.zero,
                        rendererColor = material.HasProperty("_RendererColor") ? material.GetVector("_RendererColor") : Vector4.zero };
                    var pixels = image.GetPixels32();
                    row.pixelData = pixels;
                    for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++)
                    {
                        Color32 pixel = pixels[y * 512 + x];
                        if (pixel.b <= 8) continue;
                        row.pixels++; row.minX = Math.Min(row.minX, x); row.minY = Math.Min(row.minY, y);
                        row.maxX = Math.Max(row.maxX, x); row.maxY = Math.Max(row.maxY, y);
                    }
                    row.bounded = row.pixels > 0 && row.maxX - row.minX < 24 && row.maxY - row.minY < 24;
                    if (row.pixels > 0) row.center = image.GetPixel((row.minX + row.maxX) / 2, (row.minY + row.maxY) / 2);
                    report.cases.Add(row);
                    File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
                    File.WriteAllText(Path.Combine(output, "results.json"), JsonUtility.ToJson(report, true) + "\n");
                    bool valid = expectedPixels == "small" ? row.bounded : expectedPixels == "none" ? row.pixels == 0
                        : expectedPixels == "line" ? row.pixels > 0 && row.pixels < 10000 : row.pixels > 100000;
                    if (!valid)
                        throw new Exception(name + " unexpected native GPU footprint: " + JsonUtility.ToJson(row));
                    return row;
                }
                finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
            }
            void SameRgba(Row actual, Row expected, string message)
            {
                Require(actual.pixels == expected.pixels && actual.minX == expected.minX && actual.maxX == expected.maxX
                    && actual.minY == expected.minY && actual.maxY == expected.maxY
                    && Mathf.Abs(actual.center.r - expected.center.r) < .009f
                    && Mathf.Abs(actual.center.g - expected.center.g) < .009f
                    && Mathf.Abs(actual.center.b - expected.center.b) < .009f
                    && Mathf.Abs(actual.center.a - expected.center.a) < .009f, message);
                for (int i = 0; i < actual.pixelData.Length; i++)
                {
                    Color32 a = actual.pixelData[i], b = expected.pixelData[i];
                    Require(Math.Abs(a.r - b.r) <= 2 && Math.Abs(a.g - b.g) <= 2
                        && Math.Abs(a.b - b.b) <= 2 && Math.Abs(a.a - b.a) <= 2,
                        message + " pixel=" + i);
                }
            }
            var desktop = Capture("desktop-native-sphere-original-sprite", native, sprite);
            var quest = Capture("quest-native-sphere-owned-mesh-program", native, owned, quest: true);
            SameRgba(quest, desktop, "Owned Quest pointer retains native Sprite RGBA blending and silhouette");
            Capture("quest-explicit-sphere-owned-mesh-program", explicitSphere, owned, quest: true);
            var plainOverlay = new Material(owned) { color = sprite.color };
            plainOverlay.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            var alphaControl = Capture("wrong-straight-alpha-overlay-control", native, plainOverlay, quest: true);
            Require(Mathf.Abs(alphaControl.center.a - desktop.center.a) > .1f,
                "Straight-alpha Overlay control must expose the target-alpha mismatch that this adaptation avoids");
            Object.DestroyImmediate(plainOverlay);
            var unaffected = Capture("quest-renderer-block-retains-unrelated-state", native, owned, quest: true, corruptBlock: true);
            SameRgba(unaffected, quest, "Quest ordinary mesh material ignores Sprite-only draw inputs without changing RGBA");
            sprite.SetVector("_Flip", new Vector4(100, 100, 1, 1));
            Capture("sprite-uniform-inflation-control", native, sprite, "large");
            sprite.SetVector("_Flip", Vector4.one);
            occluder.SetActive(true);
            Capture("quest-depth-occluded", native, owned, "none", quest: true);
            Capture("desktop-depth-occluded", native, sprite, "none");
            occluder.SetActive(false);
            var colors = explicitSphere.colors;
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(.6f, .3f, .8f, 1);
            explicitSphere.colors = colors;
            var desktopTint = Capture("desktop-mesh-vertex-tint", explicitSphere, sprite);
            SameRgba(Capture("quest-mesh-vertex-tint", explicitSphere, owned, quest: true), desktopTint, "Quest retains reticle mesh vertex color");
            renderer.enabled = false; line.gameObject.SetActive(true);
            var desktopLine = Capture("desktop-line-vertex-alpha", native, sprite, "line");
            SameRgba(Capture("quest-line-vertex-alpha", native, owned, "line", quest: true), desktopLine, "Quest retains the actual LineRenderer color/alpha gradient");
            Require(Mathf.Abs(factory.VertexColor(sprite, new Color(.45f, .8f, 1, .35f), false).a - .35f) < .00001f,
                "Desktop endpoint alpha remains unchanged");
            renderer.enabled = true; line.gameObject.SetActive(false);
            SameRgba(Capture("quest-final-block-repairs-foreign-block-control", native, owned, quest: true, corruptBlock: true), quest,
                "Quest repairs final sprite renderer block fields without changing RGBA or unrelated fields");
            var fallback = factory.Build(true, true);
            Require(fallback.shader == sprite.shader && fallback.renderQueue == sprite.renderQueue
                && fallback.color == sprite.color && !fallback.enableInstancing,
                "Missing owned shader retains the original visible native fallback, tint and render queue");
            SameRgba(Capture("quest-owned-shader-missing-native-fallback", native, fallback, quest: true), desktop,
                "Missing owned shader does not drop the beam/marker or change its fallback RGBA");
            Object.DestroyImmediate(fallback);
            PointerEvidence(line, sphere.transform, camera);
            Debug.Log("PASS native pointer GPU characterization: " + report.cases.Count);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally
        {
            Object.DestroyImmediate(sprite); Object.DestroyImmediate(owned); Object.DestroyImmediate(explicitSphere);
            Object.DestroyImmediate(occluder); Object.DestroyImmediate(occluderMaterial); Object.DestroyImmediate(line.gameObject);
            Object.DestroyImmediate(sphere); Object.DestroyImmediate(camera.gameObject);
            target.Release(); Object.DestroyImmediate(target);
        }
    }

    static void PointerEvidence(LineRenderer line, Transform marker, Camera camera)
    {
        int assertions = 0;
        void Require(bool value, string reason) { assertions++; if (!value) throw new Exception(reason); }
        var messages = new List<string>();
        Application.LogCallback capture = (message, stack, kind) =>
        { if (message.StartsWith("[Quest startup] pointer ", StringComparison.Ordinal)) messages.Add(message); };
        Application.logMessageReceived += capture;
        var peer = new GameObject("second-pointer-東京-😀-very-long").AddComponent<LineRenderer>();
        peer.positionCount = 2; peer.useWorldSpace = false;
        peer.SetPositions(new[] { new Vector3(.3f, -.3f, 1.5f), new Vector3(.2f, .1f, 1.5f) });
        line.gameObject.SetActive(true);
        var beforeStart = line.GetPosition(0); var beforeEnd = line.GetPosition(1);
        var beforeProjection = camera.projectionMatrix;
        var beforeMaterial = line.sharedMaterial;
        try
        {
            GloomhavenVR.Core.QuestStandalonePlatform.Enabled = false;
            Require(!GloomhavenVR.Hands.Interact.QuestPointerEvidence.NeedsObservation, "Desktop never requests pointer context lookups");
            GloomhavenVR.Hands.Interact.QuestPointerEvidence.Observe(line, marker, camera, true, false, true, true);
            Require(messages.Count == 0, "Pointer observation must stay idle outside Quest Debug real hit context");
            GloomhavenVR.Core.QuestStandalonePlatform.Enabled = true;
            GloomhavenVR.Core.VRLog.WantsDebug = false;
            GloomhavenVR.Hands.Interact.QuestPointerEvidence.Observe(line, marker, camera, true, false, true, true);
            Require(messages.Count == 0, "Pointer observation must stay idle outside Quest Debug real hit context");
            GloomhavenVR.Core.VRLog.WantsDebug = true;
            GloomhavenVR.Hands.Interact.QuestPointerEvidence.Observe(line, marker, camera, true, false, true, false);
            Require(messages.Count == 0, "Pointer observation must stay idle outside Quest Debug real hit context");
            GloomhavenVR.Hands.Interact.QuestPointerEvidence.Observe(line, marker, camera, true, false, false, true);
            Require(messages.Count == 0, "Pointer observation must stay idle outside Quest Debug real hit context");
            for (int index = 0; index < 12; index++)
                GloomhavenVR.Hands.Interact.QuestPointerEvidence.Observe(line, marker, camera, true, false, true, true);
            Require(messages.Count == 1 && messages[0].Contains("context=menu"), "Pointer evidence must retain two contexts across repeated hits and objects");
            for (int index = 0; index < 12; index++)
                GloomhavenVR.Hands.Interact.QuestPointerEvidence.Observe(line, marker, camera, true, true, false, true);
            Require(messages.Count == 2 && messages[1].Contains("context=scenario"), "Pointer evidence must retain two contexts across repeated hits and objects");
            Require(!GloomhavenVR.Hands.Interact.QuestPointerEvidence.NeedsObservation, "Completed pointer observations never request another scene lookup");
            foreach (string message in messages)
                Require(System.Text.Encoding.UTF8.GetByteCount(message) <= 500 && message.Contains("points=2")
                    && message.Contains("activeLines=2") && message.Contains("peers= second-point")
                    && message.Contains("baked=4/") && message.Contains("primary=True"),
                    "Pointer evidence must contain actual duplicate/native geometry within 500 bytes: " + message);
            Require(line.GetPosition(0) == beforeStart, "Pointer evidence must preserve start");
            Require(line.GetPosition(1) == beforeEnd, "Pointer evidence must preserve end");
            Require(line.sharedMaterial == beforeMaterial && line.enabled, "Pointer evidence must preserve renderer");
            Require(camera.projectionMatrix == beforeProjection, "Pointer evidence must preserve camera");
            Debug.Log("PASS pointer observation: " + assertions + " assertions; native two-line census, baked geometry, gates, contexts, bounds and read-only state");
        }
        finally
        {
            Application.logMessageReceived -= capture;
            Object.DestroyImmediate(peer.gameObject);
        }
    }

    static Mesh Sphere()
    {
        const int latitude = 16, longitude = 24;
        var vertices = new Vector3[(latitude + 1) * (longitude + 1)];
        var colors = new Color[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var triangles = new List<int>();
        for (int y = 0; y <= latitude; y++) for (int x = 0; x <= longitude; x++)
        {
            float phi = y * Mathf.PI / latitude, theta = x * Mathf.PI * 2 / longitude;
            int i = y * (longitude + 1) + x;
            vertices[i] = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)) * .5f;
            colors[i] = Color.white; uv[i] = new Vector2((float)x / longitude, (float)y / latitude);
            if (y == latitude || x == longitude) continue;
            int next = i + longitude + 1;
            triangles.Add(i); triangles.Add(next); triangles.Add(i + 1);
            triangles.Add(i + 1); triangles.Add(next); triangles.Add(next + 1);
        }
        var mesh = new Mesh { name = "explicit ordinary UV sphere" };
        mesh.vertices = vertices; mesh.colors = colors; mesh.uv = uv;
        mesh.triangles = triangles.ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
}
