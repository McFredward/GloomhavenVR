// GloomhavenVR companion project — floating head-avatar ("mask") asset assembler.
//
// Batch:
//   Unity -batchmode -nographics -projectPath <this> -buildTarget Win64
//         -executeMethod GloomhavenVR.HeadsBuilder.Build -logFile build-heads.log
//   (BuildAll at the tail exits the editor 0/1; do NOT pass -quit.)
//
// What it does, deterministically (no authoring in the GUI):
//  1. Imports Mask_<n>.fbx (static, no rig). The Blender prep (unity/mask-prep/
//     prepare_masks.py) already decimated each to ~4k tris, oriented it (face -> +Z,
//     up -> +Y in Unity via the FBX axis bake) and put the PIVOT at the eye midpoint,
//     at ~0.22 m tall. So the assembly here is pure: no reorientation, no collider.
//  2. Builds a material on the SELF-CONTAINED bundled GloomhavenVR/HeadUnlit shader
//     (pure unlit textured, double-sided). The albedo is a loose PNG (Mask_<n>_albedo
//     .png) extracted from the Hunyuan GLB and committed next to the FBX — the same
//     loose-texture pattern the hands/boards use. Unlit because the head floats in the
//     light-less VR void / mirror, where a scene-lit shader renders black.
//  3. Assembles Assets/Bundle/Head/Mask_<n>.prefab: the FBX instance under a root
//     named Mask_<n>, our material bound to every renderer, NO colliders. The prefab
//     origin is the eye midpoint (from the FBX pivot) so the runtime can mount it
//     directly at the tracked head-camera world pose.
//  4. Builds gloomhavenvr.bundle via AssetsBuilder (exits 0/1). Everything already
//     under Assets/Bundle/ (hands + boards) is re-collected, so they are preserved.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class HeadsBuilder
    {
        private const string Head = "Assets/Bundle/Head";
        private const string ShaderName = "GloomhavenVR/HeadUnlit"; // self-contained, bundled, unlit

        /// <summary>The mask height contract, in metres. See the assertion in AssemblePrefab for
        /// why this is a unit check with a wide band rather than a size opinion.</summary>
        private const float MinHeightMetres = 0.10f;
        private const float MaxHeightMetres = 0.60f;

        private struct HeadDef { public string Fbx, Albedo, Mat, Prefab, Root; }

        // Deterministic filename->prefab mapping (prep sorts the GLBs ascending:
        // first -> Mask_0, second -> Mask_1, third -> Mask_2).
        private static HeadDef Def(int n) => new HeadDef
        {
            Fbx    = $"{Head}/Mask_{n}.fbx",
            Albedo = $"{Head}/Mask_{n}_albedo.png",
            Mat    = $"{Head}/Mask_{n}.mat",
            Prefab = $"{Head}/Mask_{n}.prefab",
            Root   = $"Mask_{n}",
        };

        public static void Build()
        {
            try
            {
                AssetDatabase.Refresh();
                for (int n = 0; n <= 2; n++)
                {
                    var d = Def(n);
                    if (AssetImporter.GetAtPath(d.Fbx) == null)
                    {
                        Debug.LogWarning($"[GloomhavenVR] Head FBX '{d.Fbx}' not in project — skipping.");
                        continue;
                    }
                    Debug.Log($"[GloomhavenVR] === Assembling head: {d.Prefab} ===");
                    ImportModel(d);
                    Material mat = BuildMaterial(d);
                    AssemblePrefab(d, mat);
                }
                AssetsBuilder.BuildAll(); // exits the editor (0/1)
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GloomhavenVR] HeadsBuilder FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static void ImportModel(HeadDef d)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(d.Fbx)
                           ?? throw new FileNotFoundException($"Model importer not found for {d.Fbx}.");
            // THE DECLARED UNIT IS NOT TRUSTED (2026-09-02). useFileScale makes Unity honour the
            // FBX's own UnitScaleFactor header, and a delivery is free to declare whatever its
            // exporter felt like. The shipped Mask_0/Mask_1 declare 100 (centimetres); the new
            // Grimhorn delivery declares 1 (metres) — with IDENTICAL vertex coordinates, which
            // Blender confirms reads 0.230 x 0.114 x 0.220 m for both. Unity scales by
            // UnitScaleFactor/100, so the same mesh imported at 0.220 m and at 0.0022 m: a mask
            // one hundred times too small, and nothing about it would have looked like an error
            // until somebody put the headset on.
            //
            // The contract has always been "the vertex data is metres, ~0.22 m tall" (the Blender
            // prep bakes it). So take the vertex data at face value and ignore what the file
            // CLAIMS about its units. Both shipped masks are unaffected: their raw coordinates are
            // the same 0.22, so they import at the same size they always did — which the height
            // assertion in AssemblePrefab now proves rather than prints.
            importer.useFileScale = false;
            importer.globalScale = 1f;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.isReadable = true;        // prefab/bundle mesh access
            // Static prop, no rig: None keeps a plain MeshFilter/MeshRenderer and adds
            // no Animator/Avatar (the head is rigidly mounted at the head pose).
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            // We supply our own unlit material + loose PNG; the FBX embeds no textures.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.addCollider = false;      // contract: NO colliders
            importer.SaveAndReimport();
        }

        private static Material BuildMaterial(HeadDef d)
        {
            Shader shader = Shader.Find(ShaderName)
                            ?? throw new System.Exception($"Bundled shader '{ShaderName}' not found (compile error?).");
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(d.Albedo);
            if (albedo == null)
                Debug.LogWarning($"[GloomhavenVR] Albedo not found at {d.Albedo} — head will be an untextured tint.");

            var mat = new Material(shader) { name = d.Root };
            if (albedo != null) mat.SetTexture("_MainTex", albedo);
            mat.SetFloat("_Cull", 0f); // two-sided: hide decimated shell backfaces/holes
            AssetDatabase.CreateAsset(mat, d.Mat);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GloomhavenVR] Head material built: albedo={(albedo ? albedo.name : "none")}");
            return mat;
        }

        private static void AssemblePrefab(HeadDef d, Material mat)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(d.Fbx)
                        ?? throw new System.Exception($"Failed to load model at {d.Fbx}");

            // Identity prefab root = the head/camera frame (eye midpoint at origin,
            // face +Z, up +Y). The FBX already carries that pivot + orientation.
            var root = new GameObject(d.Root);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = d.Root + "_Model";
            inst.transform.SetParent(root.transform, worldPositionStays: false);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            // Flatten the model-prefab connection so the whole hierarchy saves as one
            // self-contained prefab (a nested model instance under a fresh root does not
            // SaveAsPrefabAsset cleanly in batch mode — see HandsBuilder).
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // Strip any Animator the import may have added (contract: rigid head).
            foreach (var anim in inst.GetComponentsInChildren<Animator>(true))
                Object.DestroyImmediate(anim);

            // Contract: NO colliders. Remove any that slipped in.
            foreach (var col in inst.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(col);

            // Bind our unlit material to every renderer.
            int rendererCount = 0;
            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = mr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                mr.sharedMaterials = mats;
                rendererCount++;
            }
            Debug.Log($"[GloomhavenVR] {d.Root}: {rendererCount} renderer(s) re-materialled.");

            // Log the assembled bounds so orientation/scale/pivot is auditable from the log.
            Bounds b = default; bool have = false;
            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!have) { b = mr.bounds; have = true; }
                else b.Encapsulate(mr.bounds);
            }
            if (have)
            {
                Vector3 localCenter = root.transform.InverseTransformPoint(b.center);
                Debug.Log($"[GloomhavenVR]   {d.Root} world-bounds size={b.size:F3} (height={b.size.y:F3} m), " +
                          $"center rel. to eye-pivot={localCenter:F3} (face should extend +Z, up +Y)");

                // AND THE HEIGHT IS NOW ASSERTED, NOT MERELY PRINTED (2026-09-02). The line above
                // already reported the Grimhorn delivery at 0.002 m against the other two masks'
                // 0.220 m, and a printed number that nothing checks is a number nobody reads until
                // the round is wasted. A mask is worn at the tracked head pose: too small is
                // invisible, too large swallows the player's view. Both are silent in every gate
                // this repository has except this one.
                //
                // The band is generous on purpose — it is a UNIT check, not a size opinion. An
                // artist retouching the silhouette moves this by centimetres; a wrong unit header
                // moves it by a factor of a hundred.
                if (b.size.y < MinHeightMetres || b.size.y > MaxHeightMetres)
                    throw new System.Exception(
                        $"Head '{d.Root}' imported {b.size.y:F4} m tall, outside the "
                        + $"{MinHeightMetres:F2}..{MaxHeightMetres:F2} m contract (the other masks "
                        + "are ~0.22 m). A factor of ~100 here means the FBX declares a different "
                        + "UnitScaleFactor than its vertex data implies — check the header rather "
                        + "than rescaling the mesh, because the vertex data is almost certainly "
                        + "already correct. Source: " + d.Fbx);
            }

            var saved = PrefabUtility.SaveAsPrefabAsset(root, d.Prefab, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok || saved == null)
                throw new System.Exception($"SaveAsPrefabAsset failed for {d.Prefab}");
            AssetDatabase.SaveAssets();
            Debug.Log($"[GloomhavenVR] Head prefab written: {d.Prefab}");
        }
    }
}
