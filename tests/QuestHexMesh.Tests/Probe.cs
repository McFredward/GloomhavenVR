#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Board;
using GloomhavenVR.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class QuestHexMeshProbe
{
    private static int checks;
    private static readonly List<Object> Owned = new List<Object>();
    private static T Keep<T>(T item) where T : Object { Owned.Add(item); return item; }
    private static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    public static void Run()
    {
        try
        {
            Directory.CreateDirectory("ProbeOutput");
            var volume = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
            Object.DestroyImmediate(volume.GetComponent<Collider>());
            var filter = volume.GetComponent<MeshFilter>(); var renderer = volume.GetComponent<MeshRenderer>();
            Mesh original = filter.sharedMesh;
            Check(original.vertexCount == 24 && !original.HasVertexAttribute(VertexAttribute.Color), "Actual original Cube has native interleaved inputs without COLOR");
            QuestStandalonePlatform.Enabled = false; QuestHexMesh.Bind(renderer);
            Check(filter.sharedMesh == original && volume.GetComponent<QuestHexMesh>() == null, "Desktop keeps exact native mesh and component identity");
            QuestStandalonePlatform.Enabled = true; QuestHexMesh.Bind(renderer);
            Mesh copy = filter.sharedMesh;
            Check(copy != original && volume.GetComponent<QuestHexMesh>() != null, "Native hex receives an independently owned upload");
            Check(copy.bounds == original.bounds && copy.subMeshCount == original.subMeshCount, "Actual native mesh bounds and submeshes retained");
            for (int i = 0; i < original.vertexCount; i++)
                Check(copy.vertices[i] == original.vertices[i] && copy.normals[i] == original.normals[i]
                    && copy.tangents[i] == original.tangents[i] && copy.uv[i] == original.uv[i] && copy.colors[i] == Color.white,
                    "Native position, normals, tangent, UV and explicit default COLOR retained exactly");
            int[] sourceIndices = original.triangles, actualIndices = copy.triangles;
            for (int i = 0; i < sourceIndices.Length; i++) Check(sourceIndices[i] == actualIndices[i], "Native triangle winding retained");
            for (int i = 0; i < 100; i++) QuestHexMesh.Bind(renderer);
            Check(filter.sharedMesh == copy && volume.GetComponents<QuestHexMesh>().Length == 1, "Repeated native property writes never re-upload or allocate another owner");
            volume.SetActive(false); volume.SetActive(true); QuestHexMesh.Bind(renderer);
            Check(filter.sharedMesh == copy, "Pooled hex preserves its existing owned upload");
            var material = Keep(new Material(Resources.Load<Shader>("HexDecalStable")));
            material.SetTexture("_HexMask", Resources.Load<Texture2D>("NativeHexMask"));
            material.SetFloat("_VRZTest", 4); material.SetFloat("_VRDepthBias", .0002f); material.renderQueue = 4000;
            renderer.sharedMaterial = material;
            volume.transform.localScale = new Vector3(2, .3f, 2);
            var tile = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube)); tile.transform.position = new Vector3(0, -.05f, 0); tile.transform.localScale = new Vector3(3, .1f, 3);
            var opaque = Keep(new Material(Resources.Load<Shader>("Overlay")) { color = new Color(.025f, .025f, .025f, 1) });
            opaque.SetInt("_SrcBlend", 1); opaque.SetInt("_DstBlend", 0); opaque.SetInt("_ZWrite", 1); opaque.SetInt("_VertexColor", 0); opaque.renderQueue = 2000;
            tile.GetComponent<Renderer>().sharedMaterial = opaque;
            var camera = Keep(new GameObject("actual-hex-depth-camera")).AddComponent<Camera>(); camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.nearClipPlane = .5f;
            camera.transform.position = new Vector3(3, 5, -4); camera.transform.LookAt(Vector3.zero);
            var target = Keep(new RenderTexture(160, 160, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)); target.Create(); camera.targetTexture = target;
            Color32[] Capture(string name)
            {
                camera.Render(); var prior = RenderTexture.active; var image = new Texture2D(160, 160, TextureFormat.RGBA32, false, true);
                try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 160, 160), 0, 0); image.Apply(); File.WriteAllBytes("ProbeOutput/" + name + ".png", image.EncodeToPNG()); return image.GetPixels32(); }
                finally { RenderTexture.active = prior; Object.DestroyImmediate(image); }
            }
            int Difference(Color32[] a, Color32[] b) { int n = 0; for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i].r-b[i].r)>1 || Math.Abs(a[i].g-b[i].g)>1 || Math.Abs(a[i].b-b[i].b)>1) n++; return n; }
            renderer.enabled = false; Color32[] background = Capture("tile-only"); renderer.enabled = true;
            filter.sharedMesh = original; Color32[] native = Capture("native-hex"); filter.sharedMesh = copy;
            Color32[] uploaded = Capture("owned-hex");
            Check(Difference(native, uploaded) == 0, "Actual original two-pass shader retains native GPU output after owned upload");
            Check(Difference(background, uploaded) > 100, "Actual recovered hex shader paints measured native mask/fill pixels");
            material.SetFloat("_HexIntensity", .25f); material.SetFloat("_BorderLineIntensity", .25f);
            // Retain the original shader's actual native reach uniforms.
            material.SetColor("_HexColour", new Color(.749f, .831f, .816f, .149f));
            Check(Difference(background, Capture("reach-hex")) > 100, "Original reach inputs still draw positive mask pixels");
            material.SetTexture("_HexMask", Texture2D.blackTexture); material.SetFloat("_BorderFlameIntensity", 0); material.SetFloat("_CrossHair", 0);
            Check(Difference(background, Capture("mask-control")) == 0, "Missing authored mask is detected by the actual pixel witness");
            Check(renderer.sharedMaterial == material && material.passCount == 2 && material.GetFloat("_VRZTest") == 4, "Repair leaves original shader/pass/depth/material identity intact");
            QuestHexMesh.ResetAll();
            Check(filter.sharedMesh == original && !original.HasVertexAttribute(VertexAttribute.Color), "Reset restores exact source identity and never changes shared Cube inputs");
            QuestHexMesh.Bind(renderer); Mesh replacement = Keep(Object.Instantiate(original)); filter.sharedMesh = replacement;
            QuestHexMesh.ResetAll(); Check(filter.sharedMesh == replacement, "Reset preserves a later native mesh assignment");
            Object.DestroyImmediate(volume.GetComponent<QuestHexMesh>());
            QuestHexMesh.Bind(renderer); Object.DestroyImmediate(volume);
            Check(original != null && replacement != null, "Renderer destruction releases only its owned upload");
            var neverActive = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube)); neverActive.SetActive(false);
            var neverRenderer = neverActive.GetComponent<Renderer>(); QuestHexMesh.Bind(neverRenderer);
            Mesh neverMesh = neverActive.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(neverActive); QuestHexMesh.ResetAll();
            // Destroy is deferred until the frame ends; an owned release is
            // recorded immediately even if Unity suppresses native OnDestroy.
            Check(neverMesh != original && original != null, "Never-enabled pooled ownership cannot destroy the shared source");
            QuestHexMesh.ResetAll();
            File.WriteAllText("ProbeOutput/results.json", "{\"assertions\":" + checks + ",\"graphics\":\"" + SystemInfo.graphicsDeviceType + "\",\"hardwareVerified\":false}\n");
            UnityEngine.Debug.Log("PASS native hex owned upload: " + checks); EditorApplication.Exit(0);
        }
        catch (Exception error) { UnityEngine.Debug.LogException(error); EditorApplication.Exit(1); }
        finally { QuestHexMesh.ResetAll(); foreach (Object item in Owned) if (item != null) Object.DestroyImmediate(item); }
    }
}
