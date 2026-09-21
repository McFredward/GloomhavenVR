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
            var maxSize = path.StartsWith(Root + "/Textures/", StringComparison.Ordinal) ? 1024 :
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
        static void BuildStation(GameObject root, string npc)
        {
            var furniture = new GameObject(npc == "priestess" ? "Shrine" : npc == "enchantress" ? "Workbench" : "Counter");
            furniture.transform.SetParent(root.transform, false);
            var wood = npc == "priestess" ? "PaleStone" : "DarkWood";
            if (npc == "priestess")
            {
                Box(furniture, "AltarPlinth", new Vector3(0, 0.065f, 0), new Vector3(1.35f, 0.13f, 0.64f), wood);
                foreach (var side in new[] { -1, 1 })
                {
                    var x = side * 0.43f;
                    Box(furniture, "PillarFoot" + side, new Vector3(x, 0.17f, 0), new Vector3(0.31f, 0.09f, 0.43f), wood);
                    Primitive(furniture, "StonePillar" + side, PrimitiveType.Cylinder, new Vector3(x, 0.50f, 0), new Vector3(0.24f, 0.29f, 0.32f), Mat(wood));
                    Box(furniture, "PillarCapital" + side, new Vector3(x, 0.80f, 0), new Vector3(0.32f, 0.08f, 0.43f), wood);
                }
                Box(furniture, "HangingAltarCloth", new Vector3(0, 0.56f, -0.26f), new Vector3(0.38f, 0.60f, 0.012f), "AltarCloth");
                Primitive(furniture, "AltarEmblem", PrimitiveType.Sphere, new Vector3(0, 0.59f, -0.272f), new Vector3(0.09f, 0.13f, 0.014f), Mat("Brass"));
            }
            else if (npc == "enchantress")
            {
                foreach (var x in new[] { -1, 1 }) foreach (var z in new[] { -1, 1 })
                {
                    Box(furniture, "WorkbenchLeg" + x + z, new Vector3(x * 0.63f, 0.44f, z * 0.24f), new Vector3(0.11f, 0.88f, 0.11f), wood);
                    Box(furniture, "LegBand" + x + z, new Vector3(x * 0.63f, 0.18f, z * 0.24f), new Vector3(0.119f, 0.055f, 0.119f), "Brass");
                }
                Box(furniture, "LowerShelf", new Vector3(0, 0.29f, 0), new Vector3(1.20f, 0.055f, 0.50f), wood);
                Box(furniture, "FrontApron", new Vector3(0, 0.75f, -0.25f), new Vector3(1.24f, 0.20f, 0.075f), wood);
                foreach (var side in new[] { -1, 1 })
                    Box(furniture, "SideApron" + side, new Vector3(side * 0.63f, 0.75f, 0), new Vector3(0.075f, 0.20f, 0.50f), wood);
            }
            else
            {
                Box(furniture, "FootPlinth", new Vector3(0, 0.08f, 0), new Vector3(1.42f, 0.16f, 0.66f), wood);
                for (var i = 0; i < 5; ++i)
                    Box(furniture, "FrontPanel" + i, new Vector3((i - 2) * 0.255f, 0.48f, -0.22f), new Vector3(0.25f, 0.72f, 0.09f), wood);
                foreach (var side in new[] { -1, 1 })
                {
                    Box(furniture, "SidePanel" + side, new Vector3(side * 0.635f, 0.48f, 0), new Vector3(0.09f, 0.72f, 0.49f), wood);
                    Box(furniture, "CornerPost" + side, new Vector3(side * 0.66f, 0.47f, -0.24f), new Vector3(0.11f, 0.80f, 0.11f), wood);
                    Box(furniture, "Inlay" + side, new Vector3(side * 0.66f, 0.50f, -0.300f), new Vector3(0.022f, 0.52f, 0.009f), "Brass");
                }
            }
            Box(furniture, "TopUnderLip", new Vector3(0, 0.86f, 0), new Vector3(1.46f, 0.07f, 0.69f), wood);
            for (var i = 0; i < 4; ++i)
                Box(furniture, "SurfacePlank" + i, new Vector3(0, 0.925f, (i - 1.5f) * 0.17f), new Vector3(1.5f, 0.06f, 0.166f), wood);
            Box(furniture, "FrontBrassEdge", new Vector3(0, 0.882f, -0.353f), new Vector3(1.42f, 0.018f, 0.018f), "Brass");
            var blocker = furniture.AddComponent<BoxCollider>();
            blocker.center = new Vector3(0, 0.48f, 0);
            blocker.size = new Vector3(1.50f, 0.96f, 0.72f);
            if (npc == "merchant")
            {
                foreach (var z in new[] { 0.0393f, -0.1707f })
                    Box(furniture, "CardRackLip" + z, new Vector3(0, 0.965f, z), new Vector3(0.60f, 0.020f, 0.012f), wood);
                foreach (var z in new[] { 0.145f, -0.065f })
                    Box(furniture, "CardRackSupport" + z, new Vector3(0, 1.015f, z), new Vector3(0.60f, 0.012f, 0.012f), wood);
            }
            // Native decoration is acquired from the game at runtime. Do not substitute
            // primitive candles, coins or books; physical merchandise needs the clear top.
            if (npc == "priestess")
                Box(furniture, "AltarRunner", new Vector3(0, 0.963f, 0), new Vector3(0.38f, 0.009f, 0.66f), "AltarCloth");
            if (npc == "enchantress")
                Box(furniture, "RuneMat", new Vector3(0, 0.965f, 0), new Vector3(0.55f, 0.01f, 0.40f), "AltarCloth");
        }

        static void ExpandMerchantCounter(GameObject root)
        {
            // The physical catalogue and selection tray share the counter rather than float
            // past its edge. Keep the other service furniture and map table unchanged.
            var counter = root.transform.Find("Counter");
            var lip = counter.Find("TopUnderLip");
            lip.localScale = new Vector3(1.61f, 0.07f, 0.81f);
            for (var i = 0; i < 4; i++)
            {
                var plank = counter.Find("SurfacePlank" + i);
                plank.localPosition = new Vector3(0, 0.925f, (i - 1.5f) * 0.20f);
                plank.localScale = new Vector3(1.65f, 0.06f, 0.196f);
            }
            var edge = counter.Find("FrontBrassEdge");
            edge.localPosition = new Vector3(0, 0.882f, -0.413f);
            edge.localScale = new Vector3(1.60f, 0.018f, 0.018f);

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
                    SetPosedLodBounds(actor.GetComponent<LODGroup>());
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log("TOWN_GROUND " + npc + " correction=" + bottom);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
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

        // Explicit standalone bundle build: keep the existing production bundle byte-identical.
        public static void BuildBundle()
        {
            try
            {
                // Prefab dependencies include the referenced meshes, clips, materials and textures.
                // Do not expose the FBX import roots (duplicate actors with default materials).
                var assets = Directory.GetFiles(Root + "/Prefabs", "*.prefab", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(Root + "/Shaders", "*.shader"))
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
