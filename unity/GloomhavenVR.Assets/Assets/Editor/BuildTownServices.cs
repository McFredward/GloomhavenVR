// Offline authored town-service asset builder. Use a dedicated temporary project for review.
// Never invoke the production AssetBundle build as a side effect of this authoring step.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR
{
    public static class TownServicesBuilder
    {
        const string Root = "Assets/Bundle/TownServices";
        static readonly string[] Npcs = { "merchant", "priestess", "enchantress" };
        [Serializable] public class Manifest { public MaterialRecord[] materials; }
        [Serializable] public class MaterialRecord
        {
            public string name, baseColor, normal, unityMetallicSmoothness;
            public float[] baseColorFactor;
            public float metallicFactor, roughnessFactor, normalScale;
        }
        static string Arg(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(arguments, name);
            if (index < 0 || index + 1 >= arguments.Length) throw new ArgumentException("Missing " + name);
            return Path.GetFullPath(arguments[index + 1]);
        }
        static void Folder(string path) { Directory.CreateDirectory(path); }
        static Texture2D Texture(string path, bool normal, bool linear)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !linear;
            importer.alphaIsTransparency = false;
            // Face and costume albedo retain 4K texel density. Costume micro-normal and
            // metallic masks use smaller mip ceilings to keep the self-contained bundle bounded.
            var maxSize = Path.GetFileName(path) == "hands545_albedo.png" ? 2048 :
                path.StartsWith(Root + "/Textures/", StringComparison.Ordinal) ? 1024 :
                Path.GetFileName(path).StartsWith("body_", StringComparison.Ordinal) && linear ? (normal ? 2048 : 1024) : 4096;
            importer.maxTextureSize = maxSize;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            var platform = importer.GetPlatformTextureSettings("Standalone");
            platform.overridden = true;
            platform.maxTextureSize = maxSize;
            platform.format = normal ? TextureImporterFormat.BC5 : linear ? TextureImporterFormat.DXT5 : TextureImporterFormat.BC7;
            platform.compressionQuality = normal || linear ? 100 : 50;
            importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Material Material(string name, Color color, float metallic, float smoothness)
        {
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/TownNpc.shader"));
            material.name = name;
            material.enableInstancing = true;
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            AssetDatabase.CreateAsset(material, Root + "/Materials/" + name + ".mat");
            return material;
        }
        static void ImportActor(string name, string preparedRoot, string rigRoot)
        {
            var destination = Root + "/Actors/" + name;
            Folder(destination);
            Folder(destination + "/Textures");
            var source = Path.Combine(preparedRoot, name);
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(source, "manifest.json")));
            var fbxPath = destination + "/" + name + "_rig.fbx";
            File.Copy(Path.Combine(rigRoot, name, name + "_rig.fbx"), fbxPath, false);
            foreach (var relative in manifest.materials.SelectMany(m => new[] { m.baseColor, m.normal, m.unityMetallicSmoothness }).Where(p => !String.IsNullOrEmpty(p)).Distinct())
                File.Copy(Path.Combine(source, relative), destination + "/Textures/" + Path.GetFileName(relative), false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.importAnimation = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.optimizeGameObjects = false;
            importer.skinWeights = ModelImporterSkinWeights.Custom;
            importer.maxBonesPerVertex = 4;
            importer.minBoneWeight = 0.0001f;
            // Remove redundant baked keys with a conservative bound, verified against posed vertices.
            importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            importer.animationRotationError = 0.01f;
            importer.animationPositionError = 0.01f;
            importer.animationScaleError = 0.01f;
            importer.SaveAndReimport();
            var settings = importer.defaultClipAnimations;
            foreach (var clip in settings)
            {
                foreach (var expected in new[] { "Idle", "Greeting", "Gesture", "ReturnToIdle" })
                    if (clip.name.Split('|').Last() == expected) clip.name = expected;
                clip.loopTime = clip.name == "Idle";
                clip.wrapMode = clip.loopTime ? WrapMode.Loop : WrapMode.ClampForever;
            }
            importer.clipAnimations = settings;
            importer.SaveAndReimport();
            var materials = new List<Material>();
            foreach (var record in manifest.materials)
            {
                var factor = record.baseColorFactor;
                var material = Material(name + "_" + materials.Count, new Color(factor[0], factor[1], factor[2], factor[3]).gamma,
                    record.metallicFactor, 1 - record.roughnessFactor);
                Func<string, string> texPath = p => destination + "/Textures/" + Path.GetFileName(p);
                if (!String.IsNullOrEmpty(record.baseColor)) material.mainTexture = Texture(texPath(record.baseColor), false, false);
                if (!String.IsNullOrEmpty(record.normal))
                {
                    material.SetTexture("_BumpMap", Texture(texPath(record.normal), true, true));
                    material.SetFloat("_BumpScale", record.normalScale);
                    material.EnableKeyword("_NORMALMAP");
                }
                if (!String.IsNullOrEmpty(record.unityMetallicSmoothness))
                {
                    material.SetTexture("_MetallicGlossMap", Texture(texPath(record.unityMetallicSmoothness), false, true));
                    material.SetFloat("_GlossMapScale", 1);
                    material.EnableKeyword("_METALLICGLOSSMAP");
                }
                materials.Add(material);
            }
            var actor = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath));
            actor.name = "Actor";
            var allRenderers = actor.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (allRenderers.Length != 3) throw new InvalidDataException(name + ": expected one skinned renderer per LOD");
            var lods = new LOD[3];
            for (var i = 0; i < 3; ++i)
            {
                var renderer = allRenderers.Single(r => r.name.StartsWith("LOD" + i + "_", StringComparison.Ordinal));
                renderer.sharedMaterials = materials.ToArray();
                renderer.quality = SkinQuality.Bone4;
                renderer.updateWhenOffscreen = true;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                // Authored gestures fit this bound; no whole-body or locomotion animation.
                var skinBounds = renderer.localBounds;
                skinBounds.Expand(0.40f);
                renderer.localBounds = skinBounds;
                lods[i] = new LOD(new[] { 0.50f, 0.20f, 0.04f }[i], new Renderer[] { renderer });
            }
            var group = actor.AddComponent<LODGroup>();
            group.SetLODs(lods);
            var animation = actor.GetComponent<Animation>() ?? actor.AddComponent<Animation>();
            animation.playAutomatically = true;
            animation.cullingType = AnimationCullingType.AlwaysAnimate;
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            foreach (var expected in new[] { "Idle", "Greeting", "Gesture", "ReturnToIdle" })
            {
                var clip = clips.Single(c => c.name == expected);
                clip.wrapMode = expected == "Idle" ? WrapMode.Loop : WrapMode.ClampForever;
                animation.AddClip(clip, expected);
                if (expected == "Idle") animation.clip = clip;
            }
            var stationName = "Town" + Char.ToUpperInvariant(name[0]) + name.Substring(1);
            var station = new GameObject(stationName);
            actor.transform.SetParent(station.transform, false);
            actor.transform.localPosition = new Vector3(0, 0, 0.65f);
            actor.transform.localRotation = Quaternion.Euler(0, 180, 0); // FBX imports facing +Z; station contract faces -Z.
            Anchor(station, "InteractionAnchor", new Vector3(0, 0.95f, -0.42f));
            Anchor(station, "HeadAnchor", new Vector3(0, 1.56f, 0.65f));
            Anchor(station, "ServiceSurface", new Vector3(0, 0.94f, 0));
            Anchor(station, "GroundAnchor", Vector3.zero);
            Anchor(station, "DecorAnchor", new Vector3(-0.63f, 0.96f, 0.22f));
            Anchor(station, "LightAnchor", new Vector3(-0.58f, 1.48f, 0.12f));
            BuildStation(station, name);
            if (name == "merchant") ExpandMerchantCounter(station);
            animation.GetClip("Idle").SampleAnimation(actor, 0); // Serialized opening pose already matches idle, before the first runtime tick.
            // The FBX idle pose applies its centimetre-to-metre armature scale. Calculating
            // before sampling and using Unity's skin bounds serializes a 1.75 cm LOD
            // volume around a 1.75 m actor, culling all LODs at normal distance (539).
            SetPosedLodBounds(group);
            if (group.size < 1f || group.size > 3f)
                throw new InvalidDataException(name + ": unexpected posed LOD size " + group.size);
            PrefabUtility.SaveAsPrefabAsset(station, Root + "/Prefabs/" + stationName + ".prefab");
            Debug.Log("TOWN_ASSET " + stationName + " skinnedLODs=" + allRenderers.Length + " bones=" + allRenderers[0].bones.Length +
                " triangles=" + String.Join(",", lods.Select(l => ((SkinnedMeshRenderer)l.renderers[0]).sharedMesh.triangles.Length / 3)) +
                " clips=" + String.Join(",", clips.Select(c => c.name)));
            UnityEngine.Object.DestroyImmediate(station);
        }
        static void Anchor(GameObject parent, string name, Vector3 position)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            child.transform.localPosition = position;
        }
        static Material Mat(string name) { return AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + name + ".mat"); }
        static GameObject Primitive(GameObject parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent.transform, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
            return obj;
        }
        static void Box(GameObject parent, string name, Vector3 position, Vector3 scale, string material)
        { Primitive(parent, name, PrimitiveType.Cube, position, scale, Mat(material)); }
        static GameObject AuthoredFurniture(string name)
        {
            var path = Root + "/Furniture/" + name + ".fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new FileNotFoundException("Authored town furniture is missing", path);
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.isReadable = true; // Actual mesh floor bounds are used by station grounding.
            importer.SaveAndReimport();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var furniture = UnityEngine.Object.Instantiate(source);
            foreach (var renderer in furniture.GetComponentsInChildren<MeshRenderer>(true))
            {
                var separator = renderer.name.LastIndexOf('_');
                var materialName = renderer.name.Substring(separator + 1);
                var material = Mat(materialName);
                if (material == null) throw new InvalidDataException("Unknown furniture material: " + renderer.name);
                renderer.sharedMaterial = material;
                if (renderer.name == "Handle_DarkWood") renderer.gameObject.name = "Handle";
            }
            return furniture;
        }

        static void BuildStation(GameObject root, string npc)
        {
            if (Mat("ForgedIron") == null)
                Material("ForgedIron", new Color(.065f, .060f, .050f), .85f, .24f);
            Mat("DarkWood").color = new Color(.49f, .31f, .17f);
            Mat("PaleStone").color = new Color(.79f, .74f, .65f);
            Mat("AltarCloth").color = new Color(.27f, .055f, .065f);
            foreach (var name in new[] { "DarkWood", "PaleStone", "AltarCloth" })
                EditorUtility.SetDirty(Mat(name));
            // Generated concepts are rebuilt as bevelled, curved, UV-mapped meshes. No runtime
            // procedural primitive furniture, drawer banks or decorated gameplay UI is authored here.
            var furniture = AuthoredFurniture(npc);
            furniture.name = npc == "priestess" ? "Shrine" : npc == "enchantress" ? "Workbench" : "Counter";
            furniture.transform.SetParent(root.transform, false);
            if (npc == "merchant")
            {
                var extension = AuthoredFurniture("merchant_return");
                extension.name = "CounterReturn";
                extension.transform.SetParent(furniture.transform, false);
                extension.SetActive(false); // Catalogue creates only the open wings it actually needs.
                MerchantTemplates(furniture.transform);
            }
        }

        static void MerchantTemplates(Transform counter)
        {
            foreach (var pair in new[] {
                new[] { "merchant_cassette", "MerchantCassetteTemplate" },
                new[] { "merchant_crank", "MerchantCrankTemplate" },
                new[] { "merchant_button", "MerchantButtonTemplate" } })
            {
                var template = AuthoredFurniture(pair[0]); template.name = pair[1];
                template.transform.SetParent(counter, false); template.SetActive(false);
            }
            // The two leaves fold inward under the roof. A solid panel lifted vertically
            // would rise above the portable cabinet and contradict the approved silhouette.
            var shutter = new GameObject("MerchantShutterTemplate");
            shutter.transform.SetParent(counter, false);
            var upper = new GameObject("Upper").transform;
            upper.SetParent(shutter.transform, false); upper.localPosition = new Vector3(0, .265f, 0);
            var lower = new GameObject("Lower").transform;
            lower.SetParent(upper, false); lower.localPosition = new Vector3(0, -.265f, -.020f);
            foreach (var hinge in new[] { upper, lower })
            {
                var leaf = AuthoredFurniture("merchant_shutter_leaf"); leaf.name = "Leaf";
                leaf.transform.SetParent(hinge, false);
            }
            shutter.SetActive(false);
            foreach (var pair in new[] {
                ("CabinetAnchor", new Vector3(-.95f, 0, .30f)),
                ("CabinetCassetteAnchor", new Vector3(-.95f, 1.22f, .035f)),
                ("CabinetCrankAnchor", new Vector3(-.47f, 1.05f, .11f)),
                ("CabinetShutterAnchor", new Vector3(-.95f, 1.22f, .015f)) })
                Anchor(counter.gameObject, pair.Item1, pair.Item2);
        }

        static void ExpandMerchantCounter(GameObject root)
        {
            // The side cabinet and folding lectern are authored in Furniture/merchant.fbx.
        }
        public static void RefreshMerchantCounter()
        {
            var path = Root + "/Prefabs/TownMerchant.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try { ExpandMerchantCounter(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        static void BuildWorkTray()
        {
            var tray = new GameObject("TownWorkTray");
            Box(tray, "WoodBase", new Vector3(0, -0.020f, 0), new Vector3(0.44f, 0.035f, 0.32f), "DarkWood");
            Box(tray, "LeatherContactMat", new Vector3(0, -0.0025f, 0), new Vector3(0.405f, 0.005f, 0.285f), "Leather");
            foreach (var sign in new[] { -1, 1 })
            {
                Box(tray, "LongRim" + sign, new Vector3(0, -0.005f, sign * 0.151f), new Vector3(0.44f, 0.018f, 0.018f), "DarkWood");
                Box(tray, "ShortRim" + sign, new Vector3(sign * 0.211f, -0.005f, 0), new Vector3(0.018f, 0.018f, 0.285f), "DarkWood");
            }
            foreach (var x in new[] { -1, 1 }) foreach (var z in new[] { -1, 1 })
                Primitive(tray, "BrassCorner", PrimitiveType.Sphere, new Vector3(x * 0.211f, 0.002f, z * 0.151f), new Vector3(0.009f, 0.004f, 0.009f), Mat("Brass"));
            Anchor(tray, "ContactAnchor", Vector3.zero);
            // Runtime owns interaction and collision; the asset contains presentation only.
            PrefabUtility.SaveAsPrefabAsset(tray, Root + "/Prefabs/TownWorkTray.prefab");
            UnityEngine.Object.DestroyImmediate(tray);
        }
        static void SharedMaterials(string environmentTextures)
        {
            Material("DarkWood", new Color(0.48f, 0.31f, 0.20f), 0, 0.22f);
            Material("PaleStone", new Color(0.56f, 0.52f, 0.46f), 0, 0.18f);
            Material("Brass", new Color(0.54f, 0.36f, 0.15f), 0.82f, 0.45f);
            Material("Leather", new Color(0.16f, 0.075f, 0.035f), 0, 0.22f);
            Material("Parchment", new Color(0.75f, 0.67f, 0.49f), 0, 0.16f);
            Material("AltarCloth", new Color(0.22f, 0.12f, 0.26f), 0, 0.20f);
            Material("PotionGlass", new Color(0.08f, 0.24f, 0.18f), 0.15f, 0.65f);
            Material("ArcaneCrystal", new Color(0.16f, 0.28f, 0.48f), 0.30f, 0.6f);
            var flame = Material("CandleGlow", new Color(1, 0.5f, 0.12f), 0, 0);
            flame.EnableKeyword("_EMISSION");
            flame.SetColor("_EmissionColor", new Color(1.4f, 0.65f, 0.12f));
            foreach (var pair in new[] { new[] { "DarkWood", "dark_wooden_planks" }, new[] { "PaleStone", "monastery_stone_floor" } })
            {
                foreach (var suffix in new[] { "_alb.jpg", "_nrm.jpg" })
                {
                    var file = pair[1] + suffix;
                    File.Copy(Path.Combine(environmentTextures, file), Root + "/Textures/" + file, false);
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var material = Mat(pair[0]);
                material.color = Color.white;
                material.mainTexture = Texture(Root + "/Textures/" + pair[1] + "_alb.jpg", false, false);
                material.SetTexture("_BumpMap", Texture(Root + "/Textures/" + pair[1] + "_nrm.jpg", true, true));
                material.EnableKeyword("_NORMALMAP");
            }
        }
        static void SetPosedLodBounds(LODGroup group)
        {
            // Unity 2021 RecalculateBounds uses the skinned local extent without the FBX
            // child renderer's 100x scale. Convert renderer world bounds explicitly into group
            // space; neither a forced LOD nor a guessed scalar is correct for other rigs.
            var bound = new Bounds();
            bool first = true;
            foreach (var renderer in group.GetLODs().SelectMany(l => l.renderers))
            {
                var world = renderer.bounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = group.transform.InverseTransformPoint(world.center + Vector3.Scale(world.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (first) { bound = new Bounds(point, Vector3.zero); first = false; }
                    else bound.Encapsulate(point);
                }
            }
            if (first) throw new InvalidDataException("Town actor has no LOD renderers");
            bound.Expand(0.12f); // Conservative margin for the bounded greeting/idle gesture.
            group.localReferencePoint = bound.center;
            group.size = Mathf.Max(bound.size.x, bound.size.y, bound.size.z);
        }

        static Vector3[] PosedVertices(SkinnedMeshRenderer renderer, Transform reference)
        {
            var mesh = renderer.sharedMesh; var vertices = mesh.vertices; var weights = mesh.boneWeights;
            var bind = mesh.bindposes; var bones = renderer.bones;
            var matrices = bones.Select((bone, i) => reference.worldToLocalMatrix * bone.localToWorldMatrix * bind[i]).ToArray();
            var result = new Vector3[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                var w = weights[i]; var v = vertices[i];
                result[i] = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 +
                    matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1 +
                    matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 +
                    matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
            }
            return result;
        }

        public static void RefreshPresentation()
        {
            foreach (var npc in Npcs)
            {
                var path = Root + "/Prefabs/Town" + Char.ToUpperInvariant(npc[0]) + npc.Substring(1) + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var name in new[] { "Counter", "Shrine", "Workbench", "GroundAnchor", "DecorAnchor", "LightAnchor" })
                    {
                        var old = root.transform.Find(name);
                        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                    }
                    BuildStation(root, npc);
                    if (npc == "merchant") ExpandMerchantCounter(root);
                    Anchor(root, "GroundAnchor", Vector3.zero);
                    Anchor(root, "DecorAnchor", new Vector3(-0.63f, 0.96f, 0.22f));
                    Anchor(root, "LightAnchor", new Vector3(-0.58f, 1.48f, 0.12f));
                    var actor = root.transform.Find("Actor");
                    actor.GetComponent<Animation>().GetClip("Idle").SampleAnimation(actor.gameObject, 0);
                    var renderer = actor.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name.StartsWith("LOD0_"));
                    var posed = PosedVertices(renderer, root.transform);
                    var bottom = posed.Min(v => v.y);
                    var height = posed.Max(v => v.y) - bottom;
                    if (height < 1.6f || height > 2.1f) throw new InvalidDataException("Unexpected authored actor height: " + height);
                    actor.localPosition -= new Vector3(0, bottom, 0);
                    var group = actor.GetComponent<LODGroup>();
                    if (group != null) SetPosedLodBounds(group);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log("TOWN_GROUND " + npc + " correction=" + bottom);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }

        // Replace the facial shell and its rig without rebuilding furniture or changing grounding.
        [Serializable] public class FacialContract { public int version = 1; public FacialResident[] residents; }
        [Serializable] public class FacialResident { public string npc; public FacialLod[] lods; }
        [Serializable] public class FacialLod { public string renderer; public int vertexCount; public int[] skull, jaw; }

        static int[] RegionProbes(Mesh mesh, bool jaw)
        {
            var mask = mesh.uv2;
            if (mask.Length != mesh.vertexCount) throw new InvalidDataException("Missing independent anatomical region metadata");
            var candidates = Enumerable.Range(0, mask.Length).Where(i => (jaw ? mask[i].y : mask[i].x) > .999f).ToList();
            if (candidates.Count < 6) throw new InvalidDataException("Anatomical region has too few vertices");
            var vertices = mesh.vertices; var selected = new List<int>();
            selected.Add(candidates.OrderBy(i => vertices[i].y).First());
            // Spatially spread probes include the inferior chin/beard and do not
            // derive membership from output bone weights under test.
            while (selected.Count < Math.Min(48, candidates.Count))
            {
                var next = candidates.Where(i => !selected.Contains(i)).OrderByDescending(i => selected.Min(j => (vertices[i] - vertices[j]).sqrMagnitude)).First();
                selected.Add(next);
            }
            return selected.ToArray();
        }

        public static void RefreshFacialRig()
        {
            var input = Arg("-townFaceRoot");
            var contract = new FacialContract { residents = new FacialResident[Npcs.Length] };
            Folder(Root + "/Textures");
            foreach (var npc in Npcs)
            {
                var directory = Root + "/Actors/" + npc;
                File.Copy(Path.Combine(input, npc, npc + "_rig.fbx"), directory + "/" + npc + "_rig.fbx", true);
                File.Copy(Path.Combine(input, npc, "face_albedo.png"), directory + "/Textures/face543_albedo.png", true);
            }
            var handAtlas = Path.Combine(input, "hands_albedo.png");
            if (File.Exists(handAtlas)) File.Copy(handAtlas, Root + "/Textures/hands545_albedo.png", true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var npc in Npcs)
            {
                var fbxPath = Root + "/Actors/" + npc + "/" + npc + "_rig.fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
                importer.importBlendShapes = true;
                importer.importBlendShapeNormals = ModelImporterNormals.Calculate;
                importer.SaveAndReimport();
                var settings = importer.defaultClipAnimations;
                foreach (var clip in settings)
                {
                    clip.name = clip.name.Split('|').Last();
                    clip.loopTime = clip.name == "Idle";
                    clip.wrapMode = clip.loopTime ? WrapMode.Loop : WrapMode.ClampForever;
                }
                importer.clipAnimations = settings; importer.SaveAndReimport();
                var path = Root + "/Prefabs/Town" + Char.ToUpperInvariant(npc[0]) + npc.Substring(1) + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var previous = root.transform.Find("Actor");
                    var position = previous.localPosition; var rotation = previous.localRotation; var scale = previous.localScale;
                    var materials = previous.GetComponentsInChildren<SkinnedMeshRenderer>()[0].sharedMaterials;
                    var face = materials[1];
                    if (File.Exists(Root + "/Textures/hands545_albedo.png"))
                    {
                        var handMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/TownHands545.mat");
                        if (!handMaterial)
                        {
                            handMaterial = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/TownNpc.shader"));
                            AssetDatabase.CreateAsset(handMaterial, Root + "/Materials/TownHands545.mat");
                        }
                        handMaterial.mainTexture = Texture(Root + "/Textures/hands545_albedo.png", false, false);
                        handMaterial.SetFloat("_Metallic", 0); handMaterial.SetFloat("_Glossiness", .2f);
                        materials = new[] { materials[0], face, handMaterial };
                    }
                    face.mainTexture = Texture(Root + "/Actors/" + npc + "/Textures/face543_albedo.png", false, false);
                    face.SetTexture("_BumpMap", null); face.DisableKeyword("_NORMALMAP");
                    face.SetTexture("_MetallicGlossMap", null); face.DisableKeyword("_METALLICGLOSSMAP");
                    face.SetFloat("_Metallic", 0); face.SetFloat("_Glossiness", 0.25f);
                    UnityEngine.Object.DestroyImmediate(previous.gameObject);
                    var actor = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath));
                    PrefabUtility.UnpackPrefabInstance(actor, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    actor.name = "Actor"; actor.transform.SetParent(root.transform, false);
                    actor.transform.localPosition = position; actor.transform.localRotation = rotation; actor.transform.localScale = scale;
                    // FBX imports its first animated pose, while optical/contact markers
                    // are authored in mesh bind space. Restore that space before attaching
                    // markers; otherwise an idle arm offset becomes a false palm offset.
                    var bindSkin = actor.GetComponentsInChildren<SkinnedMeshRenderer>().First();
                    var bindMatrices = bindSkin.bones.Select((bone, i) => new { Bone = bone,
                        Matrix = bindSkin.transform.localToWorldMatrix * bindSkin.sharedMesh.bindposes[i].inverse }).ToArray();
                    foreach (var binding in bindMatrices.OrderBy(binding => AnimationUtility.CalculateTransformPath(binding.Bone, actor.transform).Count(c => c == '/')))
                    {
                        binding.Bone.position = binding.Matrix.MultiplyPoint3x4(Vector3.zero);
                        binding.Bone.rotation = binding.Matrix.rotation;
                    }
                    var head = actor.GetComponentsInChildren<Transform>().Single(t => t.name == "Head");
                    var eyeMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + npc + "_eye.mat");
                    if (!eyeMaterial)
                    {
                        eyeMaterial = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/TownEye.shader"));
                        AssetDatabase.CreateAsset(eyeMaterial, Root + "/Materials/" + npc + "_eye.mat");
                    }
                    eyeMaterial.SetColor("_Color", Color.white);
                    eyeMaterial.mainTexture = Texture(Root + "/Textures/" + (npc == "enchantress" ? "green_eye.png" : "brown_eye.png"), false, false);
                    var cornea = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/TownCornea.mat");
                    if (!cornea)
                    {
                        cornea = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/TownCornea.shader"));
                        AssetDatabase.CreateAsset(cornea, Root + "/Materials/TownCornea.mat");
                    }
                    var eyeRenderers = actor.GetComponentsInChildren<MeshRenderer>();
                    if (eyeRenderers.Length != 4) throw new InvalidDataException(npc + ": expected two globes and two corneas");
                    foreach (var renderer in eyeRenderers)
                    {
                        renderer.sharedMaterial = renderer.name.EndsWith("Cornea") ? cornea : eyeMaterial;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                    }
                    foreach (var name in new[] { "EyeLeft", "EyeRight" })
                    {
                        var eye = actor.GetComponentsInChildren<Transform>().Single(t => t.name == name);
                        if (Vector3.Dot(eye.forward, actor.transform.forward) < 0.999f)
                            throw new InvalidDataException(npc + ": eye optical frame is not neutral actor +Z: " + eye.forward);
                        eye.SetParent(head, true);
                    }
                    foreach (var marker in actor.GetComponentsInChildren<Transform>().Where(t =>
                        t.name.StartsWith("PalmContact.") || t.name.StartsWith("PalmCentre.") ||
                        new[] { "ThumbTip.", "IndexTip.", "MiddleTip.", "RingTip.", "LittleTip.", "ThumbPad.", "IndexPad.", "MiddlePad.", "RingPad.", "LittlePad." }.Any(prefix => t.name.StartsWith(prefix))).ToArray())
                    {
                        var side = marker.name.Substring(marker.name.Length - 1);
                        var boneName = (marker.name.Contains("Tip.") || marker.name.Contains("Pad.")) ? marker.name.Split('.')[0].Replace("Tip", "3").Replace("Pad", "3") + "." + side : "Hand." + side;
                        var bone = actor.GetComponentsInChildren<Transform>().Single(t => t.name == boneName);
                        marker.SetParent(bone, true);
                    }
                    var mouth = new GameObject("MouthAudioAnchor").transform;
                    mouth.position = actor.transform.TransformPoint(npc == "merchant" ? new Vector3(0, 1.557f, 0.096f) :
                        npc == "priestess" ? new Vector3(0, 1.535f, 0.117f) : new Vector3(0, 1.5371f, 0.1313f));
                    mouth.rotation = actor.transform.rotation; mouth.SetParent(head, true);
                    var lods = new LOD[3];
                    var resident = new FacialResident { npc = npc, lods = new FacialLod[3] };
                    contract.residents[Array.IndexOf(Npcs, npc)] = resident;
                    for (var i = 0; i < 3; ++i)
                    {
                        var renderer = actor.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name.StartsWith("LOD" + i + "_"));
                        resident.lods[i] = new FacialLod { renderer = renderer.name, vertexCount = renderer.sharedMesh.vertexCount,
                            skull = RegionProbes(renderer.sharedMesh, false), jaw = RegionProbes(renderer.sharedMesh, true) };
                        renderer.sharedMaterials = materials.Take(renderer.sharedMesh.subMeshCount).ToArray(); renderer.quality = SkinQuality.Bone4; renderer.updateWhenOffscreen = true;
                        var bounds = renderer.localBounds; bounds.Expand(.4f); renderer.localBounds = bounds;
                        lods[i] = new LOD(new[] { .5f, .2f, .04f }[i], new Renderer[] { renderer }.Concat(eyeRenderers).ToArray());
                    }
                    var group = actor.AddComponent<LODGroup>(); group.SetLODs(lods);
                    var animation = actor.GetComponent<Animation>() ?? actor.AddComponent<Animation>();
                    animation.playAutomatically = true; animation.cullingType = AnimationCullingType.AlwaysAnimate;
                    var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
                    foreach (var name in new[] { "Idle", "Greeting", "Gesture", "ReturnToIdle" })
                    {
                        var clip = clips.Single(c => c.name == name);
                        if (AnimationUtility.GetCurveBindings(clip).Any(b => b.propertyName.StartsWith("blendShape.")))
                            throw new InvalidDataException(npc + ": body clip overrides facial weights");
                        animation.AddClip(clip, name); if (name == "Idle") animation.clip = clip;
                    }
                    animation.GetClip("Idle").SampleAnimation(actor, 0); SetPosedLodBounds(group);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            File.WriteAllText(Root + "/town-facial-rig-contract.json", JsonUtility.ToJson(contract, true));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
        }

        public static void RefreshLodBounds()
        {
            foreach (var npc in Npcs)
            {
                var name = "Town" + Char.ToUpperInvariant(npc[0]) + npc.Substring(1);
                var path = Root + "/Prefabs/" + name + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var group = root.GetComponentInChildren<LODGroup>();
                    if (group == null) { PreserveActorDetail(root); continue; }
                    SetPosedLodBounds(group);
                    if (group.size < 1f || group.size > 3f)
                        throw new InvalidDataException(name + ": unexpected posed LOD size " + group.size);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log("TOWN_LOD_BOUNDS_OK " + name + " size=" + group.size);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }

        // Build 548: NPC topology is fixed at conversational range. Hard switches between
        // independently simplified faces also changed vertex-light interpolation in VR.
        // Keep source authoring LODs in the FBX, but never ship their renderers or a group.
        static void PreserveActorDetail(GameObject root)
        {
            var actor = root.transform.Find("Actor");
            if (actor == null) throw new InvalidDataException(root.name + ": missing Actor");
            var skins = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var primary = skins.Single(r => r.name.StartsWith("LOD0_", StringComparison.Ordinal));
            foreach (var group in actor.GetComponentsInChildren<LODGroup>(true))
                UnityEngine.Object.DestroyImmediate(group);
            foreach (var skin in skins)
                if (skin != primary) UnityEngine.Object.DestroyImmediate(skin.gameObject);
            primary.enabled = true;
            foreach (var eye in actor.GetComponentsInChildren<MeshRenderer>(true)) eye.enabled = true;
        }

        public static void RefreshFixedDetail()
        {
            foreach (var npc in Npcs)
            {
                var path = Root + "/Prefabs/Town" + Char.ToUpperInvariant(npc[0]) + npc.Substring(1) + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try { PreserveActorDetail(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var contractPath = Root + "/town-facial-rig-contract.json";
            if (File.Exists(contractPath))
            {
                var contract = JsonUtility.FromJson<FacialContract>(File.ReadAllText(contractPath));
                foreach (var resident in contract.residents)
                    resident.lods = resident.lods.Where(l => l.renderer.StartsWith("LOD0_", StringComparison.Ordinal)).ToArray();
                File.WriteAllText(contractPath, JsonUtility.ToJson(contract, true));
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
        }

        // Explicit standalone bundle build: keep the existing production bundle byte-identical.
        public static void BuildBundle()
        {
            try
            {
                RefreshFixedDetail();
                // Prefab dependencies include the referenced meshes, clips, materials and textures.
                // Do not expose the FBX import roots (duplicate actors with default materials).
                var assets = Directory.GetFiles(Root + "/Prefabs", "*.prefab", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(Root + "/Shaders", "*.shader"))
                    .Concat(new[] { Root + "/town-facial-rig-contract.json" })
                    .Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToArray();
                if (assets.Length == 0) throw new InvalidOperationException("No town assets to bundle");
                Directory.CreateDirectory("Build/TownServices");
                var result = BuildPipeline.BuildAssetBundles("Build/TownServices", new[] {
                    new AssetBundleBuild { assetBundleName = "ghvr-town.bundle", assetNames = assets }
                }, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
                if (result == null) throw new InvalidOperationException("Town bundle build failed");
                var size = new FileInfo("Build/TownServices/ghvr-town.bundle").Length;
                if (size >= 100L * 1024 * 1024) throw new InvalidOperationException("Town bundle exceeds 100 MiB: " + size);
                Debug.Log("TOWN_BUNDLE_OK bytes=" + size + " assets=" + assets.Length + " TypeTrees=enabled target=StandaloneWindows64");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }
        public static void Build()
        {
            try
            {
                if (Directory.Exists(Root + "/Actors")) throw new IOException("Use a fresh authoring output; actors already exist");
                if (!File.Exists(Root + "/Shaders/TownNpc.shader")) throw new IOException("Copy TownNpc.shader into " + Root + "/Shaders first");
                foreach (var folder in new[] { "Actors", "Prefabs", "Materials", "Textures" }) Folder(Root + "/" + folder);
                AssetDatabase.Refresh();
                SharedMaterials(Arg("-townEnvironmentTextures"));
                BuildWorkTray();
                foreach (var npc in Npcs) ImportActor(npc, Arg("-townPreparedRoot"), Arg("-townRigRoot"));
                AssetDatabase.SaveAssets();
                Debug.Log("TOWN_ASSETS_BUILD_OK: three authored stations; no production bundle rebuild performed.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }
    }
}

// FBX bakes constant facial channels even though body actions contain no facial
// keys. Remove only those imported tracks; runtime owns eyes and facial weights.
internal sealed class TownFacialClipPostprocessor : AssetPostprocessor
{
    static Vector3[] FacialNormals(Vector3[] positions, int[] triangles, int[] weld, int groupCount)
    {
        var sums = new Vector3[groupCount];
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            sums[weld[a]] += normal; sums[weld[b]] += normal; sums[weld[c]] += normal;
        }
        for (var i = 0; i < sums.Length; i++)
        {
            // FBX skin vertices may be in centimetre-compensated local units;
            // Vector3.Normalize's epsilon would discard these valid tiny areas.
            float square = sums[i].sqrMagnitude;
            if (square > 1e-30f) sums[i] /= Mathf.Sqrt(square);
        }
        return weld.Select(index => sums[index]).ToArray();
    }
    void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.StartsWith("Assets/Bundle/TownServices/Actors/", StringComparison.Ordinal)) return;
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = renderer.sharedMesh;
            if (mesh.blendShapeCount == 0) continue;
            var body = Enumerable.Range(0, mesh.subMeshCount).Where(i => i != 1).SelectMany(mesh.GetIndices).Distinct().ToArray(); var face = mesh.GetIndices(1);
            var vertices = mesh.vertices; var baseNormals = mesh.normals;
            var weld = new int[vertices.Length]; var groups = new Dictionary<Vector3, int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                int group;
                if (!groups.TryGetValue(vertices[i], out group)) { group = groups.Count; groups.Add(vertices[i], group); }
                weld[i] = group;
            }
            // Use one neutral-position weld map for every expression. Opposing
            // lips/lids must not become connected when they touch during closure.
            var neutral = FacialNormals(vertices, face, weld, groups.Count);
            foreach (var index in face) baseNormals[index] = neutral[index];
            mesh.normals = baseNormals;
            var names = new List<string>(); var positions = new List<Vector3[]>(); var normals = new List<Vector3[]>();
            for (var shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                if (mesh.GetBlendShapeFrameCount(shape) != 1) throw new InvalidDataException("Expected one authored frame per facial channel");
                var p = new Vector3[mesh.vertexCount]; var n = new Vector3[mesh.vertexCount]; var t = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(shape, 0, p, n, t);
                var posed = new Vector3[vertices.Length];
                for (int i = 0; i < posed.Length; i++) posed[i] = vertices[i] + p[i];
                var posedNormals = FacialNormals(posed, face, weld, groups.Count);
                for (int i = 0; i < n.Length; i++)
                {
                    n[i] = posedNormals[i] - neutral[i];
                    if (n[i].sqrMagnitude < 1e-10f) n[i] = Vector3.zero;
                }
                foreach (var index in body)
                {
                    if (p[index].sqrMagnitude > 1e-14f) throw new InvalidDataException("Facial channel deforms costume; submesh order is wrong");
                    n[index] = Vector3.zero;
                }
                names.Add(mesh.GetBlendShapeName(shape)); positions.Add(p); normals.Add(n);
            }
            mesh.ClearBlendShapes();
            var zeroTangents = new Vector3[mesh.vertexCount];
            for (var i = 0; i < names.Count; i++) mesh.AddBlendShapeFrame(names[i], 100, positions[i], normals[i], zeroTangents);
        }
    }
    void OnPostprocessAnimation(GameObject root, AnimationClip clip)
    {
        if (!assetPath.StartsWith("Assets/Bundle/TownServices/Actors/", StringComparison.Ordinal)) return;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            if (binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal) ||
                binding.path.Split('/').Any(segment => segment == "EyeLeft" || segment == "EyeRight" ||
                    segment.StartsWith("PalmContact.") || segment.StartsWith("PalmCentre.") ||
                    new[] { "Thumb", "Index", "Middle", "Ring", "Little" }.Any(digit => segment.StartsWith(digit + "Tip.") || segment.StartsWith(digit + "Pad."))))
                AnimationUtility.SetEditorCurve(clip, binding, null);
    }
}
