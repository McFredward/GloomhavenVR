// GloomhavenVR companion project — rigged-hand asset assembler.
//
// Batch:
//   Unity -batchmode -nographics -projectPath <this> -buildTarget Win64
//         -executeMethod GloomhavenVR.HandsBuilder.Build -logFile buildhands.log
//   (BuildAll at the tail exits the editor 0/1; do NOT pass -quit.)
//
// What it does, deterministically (no hand-authoring in the GUI):
//  1. Imports VRHand_{L,R}_rig.fbx. The FBX armatures are ALREADY named to the mod's
//     rig contract (Anchor_Wrist, Anchor_Palm, Anchor_{Finger}_{Root|Mid|Tip},
//     Anchor_IndexTip, Anchor_Grab) and posed so fingers curl to the palm under
//     +local-X. We import with animationType=None (no Avatar) and optimizeGameObjects
//     OFF so every named transform survives verbatim for FindDeep-by-name.
//  2. Builds a material on the SELF-CONTAINED bundled GloomhavenVR/BoardLit shader
//     (baked studio lighting — never goes black in the light-less VR void; NOT built-in
//     Standard, which would pink-trap in a bundle). Albedo is the leather-glove base
//     colour, a loose PNG (VRHand_albedo.png) extracted from the FBX's single embedded
//     texture and committed next to the FBX — exactly the board's loose-texture pattern.
//     The FBX embeds no normal map, so the shader's flat "bump" default is used.
//  3. Assembles Assets/Bundle/Hands/VRHand_{L,R}.prefab: the FBX instance under a root
//     named VRHand_{L,R}, our material bound to the SkinnedMeshRenderer(s). Import
//     transforms are kept verbatim (the rig was authored to the contract; final device
//     offsets live in mod code, not the asset).
//  4. VERIFIES every contract bone name resolves inside each prefab (hard-fails if not).
//  5. Builds gloomhavenvr.bundle via AssetsBuilder (exits 0/1).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class HandsBuilder
    {
        private const string Hands = "Assets/Bundle/Hands";
        private const string ShaderName = "GloomhavenVR/BoardLit"; // self-contained, bundled, baked-lit
        // Leather-glove base colour, extracted from the FBX's single embedded texture and
        // committed as a loose PNG (both hands share one atlas). No normal map is embedded.
        private const string AlbedoPath = Hands + "/VRHand_albedo.png";

        // Every transform name the mod's HandVisuals.MapPrefabRig resolves by name.
        private static readonly string[] ContractBones =
        {
            "Anchor_Wrist", "Anchor_Palm", "Anchor_IndexTip", "Anchor_Grab",
            "Anchor_Thumb_Root",  "Anchor_Thumb_Mid",  "Anchor_Thumb_Tip",
            "Anchor_Index_Root",  "Anchor_Index_Mid",  "Anchor_Index_Tip",
            "Anchor_Middle_Root", "Anchor_Middle_Mid", "Anchor_Middle_Tip",
            "Anchor_Ring_Root",   "Anchor_Ring_Mid",   "Anchor_Ring_Tip",
            "Anchor_Pinky_Root",  "Anchor_Pinky_Mid",  "Anchor_Pinky_Tip",
        };

        public static void Build()
        {
            try
            {
                AssetDatabase.Refresh();
                BuildHand("VRHand_L_rig.fbx", "VRHand_L");
                BuildHand("VRHand_R_rig.fbx", "VRHand_R");
                AssetsBuilder.BuildAll(); // exits the editor (0/1)
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GloomhavenVR] HandsBuilder FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static void BuildHand(string fbxName, string rootName)
        {
            string fbx = $"{Hands}/{fbxName}";
            Debug.Log($"[GloomhavenVR] === building {rootName} from {fbx} ===");

            ImportModel(fbx);
            Material mat = BuildMaterial(rootName);
            AssemblePrefab(fbx, rootName, mat);
        }

        private static void ImportModel(string fbx)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbx)
                           ?? throw new FileNotFoundException($"Model importer not found for {fbx} — is the FBX in the project?");
            importer.useFileScale = true;      // FBX authored in metres
            importer.globalScale = 1f;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.isReadable = true;        // prefab/bundle mesh access

            // Generic rig (NOT None): "None" strips the skin cluster and imports the mesh
            // as a rigid MeshRenderer — the finger bones then deform nothing. Generic keeps
            // the SkinnedMeshRenderer bound to the armature. optimizeGameObjects MUST stay
            // OFF so the named Anchor_* transforms survive for FindDeep-by-name (an Avatar
            // with optimize ON would collapse them into the avatar). No clips to import.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.importAnimation = false;

            // We supply our own material on the bundled BoardLit shader (embedded FBX
            // materials use a non-bundled shader and would pink-trap). Textures are
            // extracted separately, below.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
        }

        private static Material BuildMaterial(string rootName)
        {
            Shader shader = Shader.Find(ShaderName)
                            ?? throw new System.Exception($"Bundled shader '{ShaderName}' not found (compile error?).");

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
            if (albedo == null)
                Debug.LogWarning($"[GloomhavenVR] Albedo not found at {AlbedoPath} — hand will be an untextured tint.");

            var mat = new Material(shader) { name = rootName };
            if (albedo != null) mat.SetTexture("_MainTex", albedo);
            // No normal map is embedded in the FBX; BoardLit's flat "bump" default is used.
            string matPath = $"{Hands}/{rootName}.mat";
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GloomhavenVR] material written: {matPath} (albedo={(albedo ? "yes" : "none")})");
            return mat;
        }

        private static void AssemblePrefab(string fbx, string rootName, Material mat)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx)
                        ?? throw new System.Exception($"Failed to load model at {fbx}");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = rootName;
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;

            // The Generic rig import adds an inert Animator (+ Avatar) we don't want: the
            // mod drives the finger joints directly (FingerCurler, "no Animator involved").
            // Strip it so the prefab is a plain skinned hand and the Avatar drops from the
            // bundle. SkinnedMeshRenderer deforms from bone transforms regardless.
            foreach (var anim in inst.GetComponentsInChildren<Animator>(true))
                Object.DestroyImmediate(anim);

            // Bind our bundled-shader material to every skinned/mesh renderer.
            int rendererCount = 0;
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                smr.sharedMaterials = mats;
                rendererCount++;
            }
            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = mr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                mr.sharedMaterials = mats;
                rendererCount++;
            }
            Debug.Log($"[GloomhavenVR] {rootName}: {rendererCount} renderer(s) re-materialled.");

            // --- verify every contract bone resolves by name ---
            var found = new Dictionary<string, Transform>();
            foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
                if (!found.ContainsKey(t.name)) found[t.name] = t;

            var missing = ContractBones.Where(n => !found.ContainsKey(n)).ToList();
            if (missing.Count > 0)
            {
                Object.DestroyImmediate(inst);
                throw new System.Exception(
                    $"{rootName}: contract bones MISSING from the imported FBX: {string.Join(", ", missing)}");
            }

            // Log each resolved bone's local frame so orientation is auditable from the
            // log alone (palm normal, finger axes) — the render is the visual check.
            Transform wrist = found["Anchor_Wrist"];
            foreach (string n in ContractBones)
            {
                Transform t = found[n];
                Vector3 lp = wrist.InverseTransformPoint(t.position);
                Debug.Log($"[GloomhavenVR]   {rootName} {n}: wrist-local pos={lp:F4}, " +
                          $"localEuler={t.localEulerAngles:F1}");
            }
            Transform palm = found["Anchor_Palm"];
            Debug.Log($"[GloomhavenVR]   {rootName} palm normal (world +Y of Anchor_Palm) = {palm.up:F3}");

            string prefabPath = $"{Hands}/{rootName}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath, out bool ok);
            Object.DestroyImmediate(inst);
            if (!ok || saved == null)
                throw new System.Exception($"SaveAsPrefabAsset failed for {prefabPath}");
            AssetDatabase.SaveAssets();
            Debug.Log($"[GloomhavenVR] prefab written + all {ContractBones.Length} contract bones resolved: {prefabPath}");
        }
    }
}
