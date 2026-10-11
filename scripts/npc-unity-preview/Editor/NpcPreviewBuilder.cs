// Standalone Unity 2021.3 editor utility. Copy into a TEMPORARY project's Assets/Editor.
// It never changes the mod's Unity asset project. Inputs are offline preparation manifests.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class NpcPreviewBuilder
{
    [Serializable] public class Manifest
    {
        public string name;
        public float unityRoughnessFloor;
        public MaterialRecord[] materials;
        public Derivative[] derivatives;
    }
    [Serializable] public class MaterialRecord
    {
        public string name, baseColor, normal, unityMetallicSmoothness, alphaMode;
        public float[] baseColorFactor;
        public float metallicFactor, roughnessFactor, normalScale, alphaCutoff;
        public bool doubleSided;
    }
    [Serializable] public class Derivative
    {
        public string name, fbx;
        public int actualTriangles;
    }

    static string Argument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 == args.Length)
            throw new ArgumentException("Missing " + name);
        return args[index + 1];
    }

    static Texture2D ImportTexture(string assetRoot, string relative, bool linear, bool normal = false)
    {
        if (String.IsNullOrEmpty(relative)) return null;
        var path = assetRoot + "/" + relative;
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !linear;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 8192;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material CreateMaterial(string assetRoot, MaterialRecord record, float floor, int index)
    {
        var material = new Material(Shader.Find("Standard"));
        material.name = record.name;
        var color = record.baseColorFactor;
        // glTF factors are linear; Unity's material color property is supplied in sRGB.
        material.color = new Color(color[0], color[1], color[2], color[3]).gamma;
        material.mainTexture = ImportTexture(assetRoot, record.baseColor, false);
        var normal = ImportTexture(assetRoot, record.normal, true, true);
        if (normal != null)
        {
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", record.normalScale);
            material.EnableKeyword("_NORMALMAP");
        }
        var mask = ImportTexture(assetRoot, record.unityMetallicSmoothness, true);
        if (mask != null)
        {
            material.SetTexture("_MetallicGlossMap", mask);
            material.SetFloat("_GlossMapScale", 1);
            material.EnableKeyword("_METALLICGLOSSMAP");
        }
        else
        {
            material.SetFloat("_Metallic", record.metallicFactor);
            material.SetFloat("_Glossiness", 1 - Mathf.Max(record.roughnessFactor, floor * (1 - record.metallicFactor)));
        }
        if (record.alphaMode == "MASK")
        {
            material.SetFloat("_Mode", 1);
            material.SetFloat("_Cutoff", record.alphaCutoff);
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }
        else if (record.alphaMode == "BLEND")
        {
            material.SetFloat("_Mode", 3);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }
        if (record.doubleSided)
            Debug.LogWarning(record.name + ": Unity Standard culls backfaces; inspect double-sided source before adoption.");
        AssetDatabase.CreateAsset(material, assetRoot + "/Materials/material_" + index.ToString("00") + ".mat");
        return material;
    }

    // -executeMethod NpcPreviewBuilder.Build -npcInputs /absolute/selected -npcPackage /absolute/Npcs.unitypackage
    // npcInputs contains one directory per NPC, each with manifest.json from prepare-npc-assets.py.
    public static void Build()
    {
        try
        {
            if (Directory.Exists("Assets/NpcPreview"))
                throw new InvalidOperationException("Use a fresh temporary project; Assets/NpcPreview already exists.");
            var input = Path.GetFullPath(Argument("-npcInputs"));
            var package = Path.GetFullPath(Argument("-npcPackage"));
            if (File.Exists(package)) throw new IOException("Package already exists: " + package);
            var manifests = Directory.GetFiles(input, "manifest.json", SearchOption.AllDirectories).OrderBy(x => x).ToArray();
            if (manifests.Length == 0) throw new InvalidOperationException("No input manifests found");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var created = new List<GameObject>();
            foreach (var path in manifests)
            {
                var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
                if (String.IsNullOrEmpty(manifest.name) || manifest.name.Any(c => !Char.IsLetterOrDigit(c) && c != '_' && c != '-'))
                    throw new InvalidDataException("Invalid NPC asset name");
                var root = "Assets/NpcPreview/" + manifest.name;
                if (Directory.Exists(root)) throw new IOException("Duplicate NPC name: " + manifest.name);
                Directory.CreateDirectory(root + "/meshes");
                Directory.CreateDirectory(root + "/textures");
                Directory.CreateDirectory(root + "/Materials");
                var source = Path.GetDirectoryName(path);
                File.Copy(path, root + "/manifest.json");
                foreach (var texture in Directory.GetFiles(Path.Combine(source, "textures"), "*.png"))
                    File.Copy(texture, root + "/textures/" + Path.GetFileName(texture));
                foreach (var derivative in manifest.derivatives)
                    File.Copy(Path.Combine(source, derivative.fbx), root + "/meshes/" + Path.GetFileName(derivative.fbx));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var materials = manifest.materials.Select((m, i) => CreateMaterial(root, m, manifest.unityRoughnessFloor, i)).ToArray();
                foreach (var derivative in manifest.derivatives)
                {
                    var assetPath = root + "/meshes/" + Path.GetFileName(derivative.fbx);
                    var importer = (ModelImporter)AssetImporter.GetAtPath(assetPath);
                    importer.importAnimation = false;
                    importer.animationType = ModelImporterAnimationType.None;
                    importer.importNormals = ModelImporterNormals.Import;
                    importer.importTangents = ModelImporterTangents.CalculateMikk;
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                    importer.useFileScale = true;
                    importer.globalScale = 1;
                    importer.SaveAndReimport();
                }
                var npc = new GameObject(manifest.name);
                var lods = new List<LOD>();
                var candidates = manifest.derivatives.Where(x => x.name.Contains("_lod")).ToArray();
                if (candidates.Length != 3) throw new InvalidDataException("Expected three LOD candidates");
                for (var i = 0; i < candidates.Length; ++i)
                {
                    var assetPath = root + "/meshes/" + Path.GetFileName(candidates[i].fbx);
                    var mesh = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(mesh);
                    instance.transform.SetParent(npc.transform, false);
                    instance.name = "LOD" + i;
                    var renderers = instance.GetComponentsInChildren<Renderer>();
                    foreach (var renderer in renderers)
                        renderer.sharedMaterials = renderer.sharedMaterials.Select(m =>
                        {
                            var slot = Array.FindIndex(manifest.materials, r => r.name == m.name);
                            if (slot < 0 && materials.Length == 1) slot = 0;
                            if (slot < 0) throw new InvalidDataException("Unmapped material: " + m.name);
                            return materials[slot];
                        }).ToArray();
                    lods.Add(new LOD(new[] { 0.45f, 0.18f, 0.04f }[i], renderers));
                }
                var group = npc.AddComponent<LODGroup>();
                group.SetLODs(lods.ToArray());
                group.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(npc, root + "/" + manifest.name + ".prefab");
                created.Add(npc);
            }
            for (var i = 0; i < created.Count; ++i)
                created[i].transform.position = new Vector3((i - (created.Count - 1) * 0.5f) * 1.4f, 0, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Preview floor";
            floor.transform.localScale = new Vector3(1, 1, 1);
            var floorMat = new Material(Shader.Find("Standard"));
            floorMat.color = new Color(0.20f, 0.22f, 0.24f);
            floorMat.SetFloat("_Glossiness", 0.1f);
            AssetDatabase.CreateAsset(floorMat, "Assets/NpcPreview/Floor.mat");
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            var key = new GameObject("Key light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.1f;
            key.transform.rotation = Quaternion.Euler(35, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.35f);
            var camera = new GameObject("Preview camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 1.1f, -5);
            camera.transform.LookAt(new Vector3(0, 0.9f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.12f, 0.15f);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/NpcPreview/NpcPreview.unity");
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(package));
            AssetDatabase.ExportPackage("Assets/NpcPreview", package, ExportPackageOptions.Recurse);
            Debug.Log("NPC preview package exported: " + package + "; static LOD candidates require visual review.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }
}
