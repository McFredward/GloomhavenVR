// GloomhavenVR companion project — rigged-hand asset assembler.
//
// Batch:
//   Unity -batchmode -nographics -projectPath <this> -buildTarget Win64
//         -executeMethod GloomhavenVR.HandsBuilder.Build -logFile buildhands.log
//   (BuildAll at the tail exits the editor 0/1; do NOT pass -quit.)
//
// What it does, deterministically (no hand-authoring in the GUI):
//  1. Imports each hand set's {base}_{L,R}_rig.fbx — three selectable sets ship:
//     VRHand (leather glove), VRHandPlate (plate gauntlet), VRHandArcane (mage glove);
//     the mod's [Hands] HandStyle setting picks the pair at runtime (HandVisuals.cs).
//     The FBX armatures are ALREADY named to the mod's
//     rig contract (Anchor_Wrist, Anchor_Palm, Anchor_{Finger}_{Root|Mid|Tip},
//     Anchor_IndexTip, Anchor_Grab) and posed so fingers curl to the palm under
//     +local-X. We import with animationType=None (no Avatar) and optimizeGameObjects
//     OFF so every named transform survives verbatim for FindDeep-by-name.
//  2. Builds a material on the SELF-CONTAINED bundled GloomhavenVR/BoardLit shader
//     (baked studio lighting — never goes black in the light-less VR void; NOT built-in
//     Standard, which would pink-trap in a bundle). Albedo is the leather-glove base
//     colour, a loose PNG (VRHand_albedo.png) extracted from the FBX's single embedded
//     texture and committed next to the FBX — exactly the board's loose-texture pattern.
//     A set may also ship a loose tangent-space NORMAL map (the arcane one does); a set
//     without one uses the shader's flat "bump" default, as all three did up to ModBuild 170.
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
        // Per-set albedo and optional normal map, committed as loose PNGs (each L/R pair shares
        // one atlas — the mirrored hand samples the same texels).
        //   VRHand       — leather glove (default style); artist-authored mesh + rig,
        //                  adopted via unity/hand-prep/import_glove_fbx.py
        //   VRHandPlate  — plate-armor gauntlet   (artist-authored mesh + rig, same script)
        //   VRHandArcane — arcane-runes mage glove (same pipeline)
        // As of ModBuild 243 NO hand set is AI output any more; the plate gauntlet was the last
        // one, and prepare_hand.py / rig_hand.py no longer produce anything that ships.
        //
        // doubleSided: render the set with Cull Off. This is a REPAIR, not a look — an
        // AI-generated shell is fragmented and non-manifold, so back-facing and missing
        // patches read as black voids under ordinary back-face culling (worst on the
        // middle finger), and BoardLit's VFACE path lights the back faces so a hole shows
        // the surface behind it instead. It costs a second shaded fragment over the whole
        // hand, in both eyes, every frame — so it is per set, and set from a MEASUREMENT
        // (unity/hand-prep, boundary + non-manifold edge counts on the shipped rigs):
        //   VRHand       0 boundary,   0 non-manifold, +298 cm3  -> closed, wound outward
        //   VRHandArcane 0 boundary,   0 non-manifold, +1748 cm3 -> closed, wound outward
        //   VRHandPlate  0 boundary,   0 non-manifold, +1526 cm3 -> closed, wound outward
        //                (was 540 / 1072 on the AI shell, up to ModBuild 242)
        // A closed, outward-wound shell has no hole to fill, so it pays nothing — and with
        // the plate gauntlet replaced, EVERY set is false and the repair is now dead weight
        // no one carries. Kept as a flag rather than deleted because it is the only thing
        // standing between a future fragmented mesh and a hand full of black voids; the
        // measurement above is what turns it on, never a look.
        //
        // normal: an artist-authored tangent-space normal map, or null for none. BoardLit has
        // always declared _BumpMap and _NormalStrength and read TANGENT in its vertex input; up
        // to ModBuild 170 no hand set supplied one, so every hand rendered on the shader's flat
        // "bump" default and carried only what the albedo had baked into it. The arcane set brought
        // the first (171) and the glove followed with a re-baked albedo of its own (172). The plate
        // gauntlet was flat-bumped from 243 to 247 because its first delivery was a mesh and a base
        // colour and nothing else — BoardLit's flat default being the honest reading of that, since
        // inventing a map from the albedo's luminance would emboss the painted rivets and the
        // painted shadows alike. THE SECOND PLATE DELIVERY (ModBuild 248) BRINGS ONE, so every set
        // now carries a real map and the null column is empty. The importer is told the texture is
        // a NORMAL MAP (below) — leaving it as a plain colour texture is the silent version of this
        // failure: it samples, it looks roughly right, and every slope is wrong.
        //
        // The arcane set also delivered a DISPLACEMENT map. It is deliberately NOT shipped:
        // BoardLit has no height, parallax or tessellation term, and the hands are not
        // subdivided, so there is nothing in this pipeline that could read it. Adding it would
        // put 3.4 MB in the bundle for no pixel.
        //
        // mrs: THE PLATE'S METALLIC AND ROUGHNESS, PACKED — and the displacement rule does NOT
        // extend to them, because at ModBuild 248 the shader grew the term that reads them. The
        // second plate delivery is a full PBR set, and its base colour is FLAT BY DESIGN: the
        // hammered-steel micro-detail that the first delivery had baked into the albedo now lives
        // in the normal and roughness maps. Shipping albedo+normal alone was measured and rejected
        // on that basis — the delivery got better and the picture got worse, a lighter, flatter
        // gauntlet than the asset it replaced. So BoardLit gained an OPT-IN Blinn-Phong lobe
        // against the same two baked directions its diffuse already uses, and this column is the
        // opt-in: R = metallic, G = roughness, packed by unity/hand-prep/pack_mrs.py, imported as
        // LINEAR data (sRGB off — see ImportAsLinearData; a metallic map read through the sRGB
        // curve is wrong at every value except 0 and 1, and wrong silently).
        //
        // A null here is not a degraded path, it is the ZERO STATE: _SpecStrength stays at its 0
        // default and the shader's specular branch does not execute, so the leather glove, the
        // arcane glove and both control boards are bit-identical to the build before this existed.
        // That is what makes it safe for one asset's delivery to change a SHARED shader.
        //
        // THE PLATE ATLAS IS NO LONGER RESAMPLED (ModBuild 248). The FIRST delivery arrived
        // 1254x1254 — not a power of two AND not a multiple of four, so Unity could block-compress
        // none of it — and was committed upscaled to 2048x2048 (Lanczos) so that DXT1 + mips cost
        // ~2.8 MB instead of ~8.4 MB of RGBA32, while discarding no delivered pixel. THE SECOND
        // DELIVERY IS NATIVELY 2048x2048 and needs none of that: it is committed exactly as
        // delivered, and unlike its predecessor those are 2048 real texels rather than 1254
        // upscaled. It also arrives with its UV islands DILATED into the background instead of
        // sitting on black, which is what stops an island's edge from bleeding void into itself at
        // the lower mips — the reason to take this delivery even where the pixels look the same.
        //
        // normalStrength: BoardLit's _NormalStrength, which scales the sampled normal's XY
        // (BoardLit.shader:118). 1.0 is "as authored" and is the shader's default, so a set that
        // wants it pays nothing for saying so. THE LEATHER GLOVE SHIPS 0.5, on his hardware note
        // of 2026-09-08: "bitte verringe die Stärke der normal-map auf die Hälfte, die Finger
        // sehen so zerknittert aus sonst." The map is fine; the viewing distance is the point. A
        // hand is read at arm's length in a headset, and at that distance the baked leather
        // creases stop being leather and start being wrinkled skin. It is a property of THIS
        // delivery at THAT distance, which is why it is a per-set number here and not a dial:
        // the same texture is wrong by the same factor for every player, and a hand's look is
        // mirrored onto every peer (Net.Remote.RemoteAvatar builds a peer's hands through the
        // same HandVisuals.Build), so a viewer-local dial here would be a 1:1 breach.
        // The plate and arcane sets are untouched: their maps were authored against their own
        // surfaces and neither was reported.
        private static readonly (string baseName, string albedo, string normal, string mrs,
                                 bool doubleSided, float normalStrength)[] HandSets =
        {
            ("VRHand",       Hands + "/VRHand_albedo.png",       Hands + "/VRHand_normal.png",       null,                           false, 0.5f),
            ("VRHandPlate",  Hands + "/VRHandPlate_albedo.png",  Hands + "/VRHandPlate_normal.png",  Hands + "/VRHandPlate_mrs.png", false, 1.0f),
            ("VRHandArcane", Hands + "/VRHandArcane_albedo.png", Hands + "/VRHandArcane_normal.png", null,                           false, 1.0f),
        };

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
                foreach ((string baseName, string albedo, string normal, string mrs,
                          bool doubleSided, float normalStrength) in HandSets)
                {
                    BuildHand($"{baseName}_L_rig.fbx", $"{baseName}_L", albedo, normal, mrs, doubleSided, normalStrength);
                    BuildHand($"{baseName}_R_rig.fbx", $"{baseName}_R", albedo, normal, mrs, doubleSided, normalStrength);
                }
                AssetsBuilder.BuildAll(); // exits the editor (0/1)
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GloomhavenVR] HandsBuilder FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static void BuildHand(string fbxName, string rootName, string albedoPath,
                                      string normalPath, string mrsPath, bool doubleSided,
                                      float normalStrength)
        {
            string fbx = $"{Hands}/{fbxName}";
            Debug.Log($"[GloomhavenVR] === building {rootName} from {fbx} ===");

            ImportModel(fbx);
            Material mat = BuildMaterial(rootName, albedoPath, normalPath, mrsPath, doubleSided,
                                         normalStrength);
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
            // TANGENTS: required by any set carrying a normal map — BoardLit reads TANGENT in its
            // vertex input and builds the world tangent frame from it. Calculated rather than
            // imported, so it is derived from the SAME UVs the map was baked against whatever the
            // exporter wrote; a set with no normal map pays nothing for having them.
            importer.importTangents = ModelImporterTangents.CalculateMikk;
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

        /// <summary>Force a texture to import as a tangent-space NORMAL MAP. Unity decides this
        /// from the .meta, and a normal map left as a plain colour texture is the silent kind of
        /// wrong: it samples, the hand looks roughly lit, and every slope on it is inverted or
        /// flattened. Idempotent — only reimports when the setting actually has to change.</summary>
        private static void ImportAsNormalMap(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                Debug.LogWarning($"[GloomhavenVR] no TextureImporter at {path} — normal map skipped.");
                return;
            }
            if (ti.textureType == TextureImporterType.NormalMap)
                return;
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
            Debug.Log($"[GloomhavenVR] {path} re-imported as a NormalMap.");
        }

        // A metallic/roughness pack is DATA, not colour. Left on the importer's sRGB default every
        // intermediate value is read through the sRGB curve and is wrong — and wrong SILENTLY: the
        // texture samples, the highlight appears, and only 0 and 1 come out right. This is the same
        // failure shape as leaving a normal map typed as a colour texture, which is why it is
        // forced here rather than trusted to a committed .meta.
        private static void ImportAsLinearData(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                Debug.LogWarning($"[GloomhavenVR] no TextureImporter at {path} — MRS pack skipped.");
                return;
            }
            if (ti.textureType == TextureImporterType.Default && !ti.sRGBTexture)
                return;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;
            ti.SaveAndReimport();
            Debug.Log($"[GloomhavenVR] {path} re-imported as LINEAR data (sRGB off).");
        }

        private static Material BuildMaterial(string rootName, string albedoPath, string normalPath,
                                              string mrsPath, bool doubleSided, float normalStrength)
        {
            Shader shader = Shader.Find(ShaderName)
                            ?? throw new System.Exception($"Bundled shader '{ShaderName}' not found (compile error?).");

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            if (albedo == null)
                Debug.LogWarning($"[GloomhavenVR] Albedo not found at {albedoPath} — hand will be an untextured tint.");

            Texture2D normal = null;
            if (!string.IsNullOrEmpty(normalPath))
            {
                ImportAsNormalMap(normalPath);
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                if (normal == null)
                    Debug.LogWarning($"[GloomhavenVR] Normal map not found at {normalPath} — the "
                                     + "hand falls back to BoardLit's flat bump.");
            }

            Texture2D mrs = null;
            if (!string.IsNullOrEmpty(mrsPath))
            {
                ImportAsLinearData(mrsPath);
                mrs = AssetDatabase.LoadAssetAtPath<Texture2D>(mrsPath);
                if (mrs == null)
                    Debug.LogWarning($"[GloomhavenVR] MRS pack not found at {mrsPath} — the hand "
                                     + "keeps BoardLit's zero specular.");
            }

            var mat = new Material(shader) { name = rootName };
            if (albedo != null) mat.SetTexture("_MainTex", albedo);
            if (normal != null) mat.SetTexture("_BumpMap", normal);
            // Written ONLY when the set asks for something other than the shader's 1.0, so a set
            // at "as authored" produces a material byte-identical to every build before this
            // parameter existed — the same opt-in shape _SpecStrength uses above. See HandSets
            // for why the glove is 0.5 and why it is not a dial.
            if (!Mathf.Approximately(normalStrength, 1f))
                mat.SetFloat("_NormalStrength", normalStrength);
            // SPECULAR IS OPT-IN, AND THE OPT-IN IS THE MAP (ModBuild 248). A set that delivers
            // no metallic/roughness pack leaves _SpecStrength at BoardLit's 0 default, where the
            // shader's specular branch does not execute at all — so the leather glove, the arcane
            // glove and both control boards render BIT-IDENTICALLY to the build before this
            // existed. 1.0 is the neutral weight, "as authored"; it is a material float precisely
            // so that a hardware round can say "too hot" and be answered without rebuilding
            // anything else.
            if (mrs != null)
            {
                mat.SetTexture("_MRSMap", mrs);
                mat.SetFloat("_SpecStrength", 1f);
            }
            // DEFECT 2 FIX — Cull Off for the AI-generated shells only; see HandSets for
            // the measurement that decides it per set. 0 = CullMode.Off, 2 = CullMode.Back.
            mat.SetFloat("_Cull", doubleSided ? 0f : 2f);
            // A set without a normal map keeps BoardLit's flat "bump" default (see HandSets).
            string matPath = $"{Hands}/{rootName}.mat";
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GloomhavenVR] material written: {matPath} "
                      + $"(albedo={(albedo ? "yes" : "none")}, normal={(normal ? "yes" : "flat")}, "
                      + $"specular={(mrs ? "metallic/roughness pack" : "off")}, "
                      + $"normalStrength={normalStrength:0.00}, "
                      + $"cull={(doubleSided ? "Off" : "Back")})");
            return mat;
        }

        // PRIORITY-3 (opt-in) — dark backing-core material. When the rig FBX was built with
        // RIG_HAND_CORE=1 it carries a second, watertight skinned mesh named
        // "VRHand_{L,R}_core" (voxel-remeshed, inset a few mm inside the textured outer
        // shell). It is rendered in a flat dark leather tint (BoardLit with no albedo texture,
        // just _Color) so any residual see-through hole in the fragmented outer mesh reveals
        // this dark core rather than the background. Double-sided so it reads from either side.
        // Only created lazily when a "core" renderer is actually present (default builds have
        // none, so the prefab is byte-identical to the two-sided-only hand).
        private static Material BuildCoreMaterial(string rootName)
        {
            Shader shader = Shader.Find(ShaderName)
                            ?? throw new System.Exception($"Bundled shader '{ShaderName}' not found (compile error?).");
            var mat = new Material(shader) { name = rootName + "_core" };
            mat.SetColor("_Color", new Color(0.10f, 0.075f, 0.055f, 1f)); // dark leather
            mat.SetFloat("_Cull", 0f);
            string matPath = $"{Hands}/{rootName}_core.mat";
            AssetDatabase.CreateAsset(mat, matPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GloomhavenVR] core material written: {matPath}");
            return mat;
        }

        // DEFECT 1 FIX — prefab-root orientation correction.
        // The rig FBX is authored in Blender's frame (+Z along fingers, +Y back of hand)
        // and exported with axis_up='Y', axis_forward='-Z'. Unity's FBX importer lands
        // that content pitched 90° about X: fingers end up along +Y and the back of the
        // hand faces -Z (render-verified — see hand-rig-report.md §Known risk 1). The mod
        // mounts the prefab at identity under the OpenXR grip frame (+Z forward, +Y up),
        // so the wrist frame must have fingers along +Z / back along +Y. We keep the FBX's
        // internal rig untouched (the FingerCurler axes, palm normal and mirroring all
        // round-trip correctly) and simply rotate the whole model +90° about X UNDER an
        // identity prefab root, so the ROOT is the wrist frame (+Z fingers, +Y back) exactly
        // as the contract (README §Conventions) requires.
        private static readonly Quaternion OrientationFix = Quaternion.Euler(90f, 0f, 0f);

        private static void AssemblePrefab(string fbx, string rootName, Material mat)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx)
                        ?? throw new System.Exception($"Failed to load model at {fbx}");

            // Identity prefab root = the wrist/grip frame the mod mounts at.
            var root = new GameObject(rootName);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = rootName + "_Model";
            inst.transform.SetParent(root.transform, worldPositionStays: false);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = OrientationFix; // pitch the whole hand back onto +Z fingers
            inst.transform.localScale = Vector3.one;
            // Flatten the model-prefab connection so the whole hierarchy saves as one
            // self-contained prefab (a nested model-instance under a fresh root does not
            // SaveAsPrefabAsset cleanly in batch mode).
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // The Generic rig import adds an inert Animator (+ Avatar) we don't want: the
            // mod drives the finger joints directly (FingerCurler, "no Animator involved").
            // Strip it so the prefab is a plain skinned hand and the Avatar drops from the
            // bundle. SkinnedMeshRenderer deforms from bone transforms regardless.
            foreach (var anim in inst.GetComponentsInChildren<Animator>(true))
                Object.DestroyImmediate(anim);

            // Bind our bundled-shader material to every skinned/mesh renderer. The optional
            // dark backing core (renderer name contains "core") gets its own dark material,
            // built lazily only if such a renderer is present (see BuildCoreMaterial).
            Material coreMat = null;
            System.Func<string, Material> pick = name =>
            {
                if (!name.Contains("core")) return mat;
                return coreMat ??= BuildCoreMaterial(rootName);
            };
            int rendererCount = 0;
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Material bind = pick(smr.name);
                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = bind;
                smr.sharedMaterials = mats;
                rendererCount++;
            }
            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material bind = pick(mr.name);
                var mats = mr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = bind;
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
                Object.DestroyImmediate(root);
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
            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok || saved == null)
                throw new System.Exception($"SaveAsPrefabAsset failed for {prefabPath}");
            AssetDatabase.SaveAssets();
            Debug.Log($"[GloomhavenVR] prefab written + all {ContractBones.Length} contract bones resolved: {prefabPath}");
        }
    }
}
