// GloomhavenVR companion project — control-board (PlayTray) asset assembler.
//
// Batch:
//   Unity -batchmode -projectPath <this> -buildTarget Win64
//         -executeMethod GloomhavenVR.BoardBuilder.Build -logFile board-build.log
//   (run WITH graphics on an X server if you want the preview render; add -nographics
//    to skip the render and only assemble + bundle.)
//
// What it does, deterministically (no hand-authoring in the GUI):
//  1. Imports PlayTray_prepped.fbx (embedded textures), extracts + remaps them,
//     marks the normal map, and sets metres scale.
//  2. Builds a material on the bundled GloomhavenVR/BoardLit shader (albedo + normal).
//  3. Assembles Assets/Bundle/Table/PlayTray.prefab: a "PlayTray" root with the model
//     oriented to the bundle contract — board in local XY, decorated face toward -Z,
//     body z>0 — computed FROM THE ANCHOR AXES (Slot1/Slot2 span the long axis, the
//     rest pads span the short axis, their cross product is the decorated normal), so
//     it is correct regardless of FBX axis quirks. Verifies the six named anchors.
//  4. Optionally renders a viewer-side preview PNG (skipped under -nographics).
//  5. Builds gloomhavenvr.bundle via AssetsBuilder.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class BoardBuilder
    {
        private const string Table = "Assets/Bundle/Table";
        private const string Fbx = Table + "/PlayTray_prepped.fbx";
        private const string ShaderName = "GloomhavenVR/BoardLit";
        private const string MatPath = Table + "/PlayTray.mat";
        private const string PrefabPath = Table + "/PlayTray.prefab";
        private const string AlbedoPath = Table + "/PlayTray_albedo.png";
        private const string NormalPath = Table + "/PlayTray_normal.png";
        private const string PreviewPng = "board-unity-preview.png";

        private static readonly string[] AnchorNames =
            { "Slot1", "Slot2", "ShortRestToken", "LongRestToken", "ConfirmButton", "UndoButton" };

        public static void Build()
        {
            try
            {
                AssetDatabase.Refresh();
                ImportModel();
                Material mat = BuildMaterial();
                GameObject prefab = AssemblePrefab(mat);
                TryRenderPreview(prefab);
                AssetsBuilder.BuildAll(); // exits the editor (0/1)
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GloomhavenVR] BoardBuilder FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static void ImportModel()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            if (importer == null)
                throw new FileNotFoundException($"Model importer not found for {Fbx} — is the FBX in the project?");
            importer.useFileScale = true;          // FBX is authored in metres
            importer.globalScale = 1f;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.isReadable = true;            // prefab/bundle mesh access
            // We supply our own material (BoardLit) + loose PNG textures, so the FBX's
            // embedded materials/textures are not imported (avoids a stray .fbm blob).
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();

            // The albedo/normal are loose PNGs next to the FBX (extracted from the GLB).
            // Mark the normal map so the shader's tangent-space unpack is correct.
            var nti = (TextureImporter)AssetImporter.GetAtPath(NormalPath);
            if (nti != null && nti.textureType != TextureImporterType.NormalMap)
            {
                nti.textureType = TextureImporterType.NormalMap;
                nti.SaveAndReimport();
            }
        }

        private static Material BuildMaterial()
        {
            Shader shader = Shader.Find(ShaderName)
                            ?? throw new System.Exception($"Bundled shader '{ShaderName}' not found (compile error?).");

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
            if (albedo == null)
                Debug.LogWarning("[GloomhavenVR] Albedo not found at " + AlbedoPath + " — board will be untextured tint.");

            var mat = new Material(shader) { name = "PlayTray" };
            if (albedo != null) mat.SetTexture("_MainTex", albedo);
            if (normal != null) mat.SetTexture("_BumpMap", normal);
            AssetDatabase.CreateAsset(mat, MatPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GloomhavenVR] Material built: albedo={(albedo ? albedo.name : "none")}, normal={(normal ? normal.name : "none")}");
            return mat;
        }

        private static GameObject AssemblePrefab(Material mat)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (model == null) throw new System.Exception($"Failed to load model at {Fbx}");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "Board";

            // --- resolve the six anchors (any depth, by name) ---
            var anchors = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
                if (System.Array.IndexOf(AnchorNames, t.name) >= 0 && !anchors.ContainsKey(t.name))
                    anchors[t.name] = t;
            foreach (string n in AnchorNames)
                if (!anchors.ContainsKey(n))
                    Debug.LogWarning($"[GloomhavenVR] Anchor '{n}' MISSING from the model — the mod will synthesize a procedural one.");

            // --- deterministic orientation from the anchor frame ---
            // Slot1->Slot2 = board long (X) axis; rest pads span the short (Y) axis;
            // their cross product is the DECORATED-face normal (from the prep, +Z in
            // Blender). Contract target: long->+X, decorated normal->-Z (toward the
            // viewer, HMD on -Z). A pure rotation then sends the short axis to -Y.
            if (anchors.ContainsKey("Slot1") && anchors.ContainsKey("Slot2")
                && anchors.ContainsKey("ShortRestToken") && anchors.ContainsKey("LongRestToken"))
            {
                Vector3 u = (anchors["Slot2"].position - anchors["Slot1"].position).normalized;      // long axis
                Vector3 s = (anchors["ShortRestToken"].position - anchors["LongRestToken"].position).normalized; // short axis
                Vector3 n = Vector3.Cross(u, s).normalized;                                           // decorated normal
                // Re-orthogonalize s against u (guard against non-perpendicular anchors).
                Vector3 v = Vector3.Cross(n, u).normalized;

                // n = cross(u,v) points out the UNDECORATED back (verified by render):
                // the decorated face (slots/pads) is -n, and the contract wants it
                // toward -Z (the viewer). So map n -> +Z, which puts -n (decorated) -> -Z.
                // (u -> +X keeps the rest zone left / buttons right; v -> +Y then follows
                // as a proper rotation.)
                var src = new Matrix4x4();
                src.SetColumn(0, u); src.SetColumn(1, v); src.SetColumn(2, n); src.SetColumn(3, new Vector4(0,0,0,1));
                var dst = new Matrix4x4();
                dst.SetColumn(0, Vector3.right); dst.SetColumn(1, Vector3.up); dst.SetColumn(2, Vector3.forward);
                dst.SetColumn(3, new Vector4(0,0,0,1));
                Quaternion rot = (dst * src.transpose).rotation;
                inst.transform.rotation = rot;

                // GUARANTEE the anchor plane is the local XY plane with the decorated
                // face toward -Z (the viewer). The matrix above can leave a residual 90°
                // (the FBX Y-up import lands the plane in XZ, normal along Y) which made
                // cards stand UPRIGHT on the board instead of lying flat. Snap the
                // decorated-back normal exactly onto +Z; the shortest-arc rotation keeps
                // the long axis (Slot1->Slot2) in place.
                Vector3 nCur = Vector3.Cross(
                    anchors["Slot2"].position - anchors["Slot1"].position,
                    anchors["ShortRestToken"].position - anchors["LongRestToken"].position).normalized;
                inst.transform.rotation = Quaternion.FromToRotation(nCur, Vector3.forward) * inst.transform.rotation;
            }
            else
            {
                Debug.LogWarning("[GloomhavenVR] Slot/rest anchors missing — falling back to Euler(90,0,0) orientation.");
                inst.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }

            // --- centre the anchor plane at local z=0, body toward +z, XY-centred ---
            Vector3 planeCentre = Vector3.zero;
            int c = 0;
            foreach (var kv in anchors) { planeCentre += kv.Value.position; c++; }
            if (c > 0) planeCentre /= c;
            inst.transform.position -= planeCentre; // anchors' centroid -> origin

            // If the solid body ended up in FRONT of the cards (min z < 0 well past the
            // anchor plane), flip 180° about Y so the body sits behind (z>0) and the
            // decorated face still points -Z.
            Bounds b = LocalBounds(inst);
            if (b.center.z < -0.002f)
            {
                inst.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * inst.transform.rotation;
                inst.transform.position = Vector3.zero;
                planeCentre = Vector3.zero; c = 0;
                foreach (var kv in anchors) { planeCentre += kv.Value.position; c++; }
                if (c > 0) inst.transform.position -= planeCentre / c;
                b = LocalBounds(inst);
                Debug.Log("[GloomhavenVR] Body was in front of the cards — flipped 180° about Y.");
            }

            // --- material onto every renderer ---
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }

            // --- wrap under the contract root "PlayTray" ---
            var root = new GameObject("PlayTray");
            inst.transform.SetParent(root.transform, worldPositionStays: true);

            // Log the resolved geometry so orientation is verifiable from the log alone.
            Debug.Log($"[GloomhavenVR] Board bounds (local): center={b.center}, size={b.size}");
            foreach (string n in AnchorNames)
                if (anchors.ContainsKey(n))
                    Debug.Log($"[GloomhavenVR]   anchor {n} local = {root.transform.InverseTransformPoint(anchors[n].position)}");

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            if (!ok) throw new System.Exception($"SaveAsPrefabAsset failed for {PrefabPath}");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GloomhavenVR] Prefab written: {PrefabPath}");
            return saved;
        }

        private static Bounds LocalBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<MeshRenderer>(true);
            if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
            // world bounds -> local (go has the rotation applied; approximate with world here)
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            b.center = go.transform.InverseTransformPoint(b.center);
            return b;
        }

        private static void TryRenderPreview(GameObject prefab)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.Log("[GloomhavenVR] No graphics device (-nographics) — skipping preview render.");
                return;
            }
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                // Tilt like the mod does (TrayTilt ~30°, -Z up toward the viewer).
                inst.transform.rotation = Quaternion.Euler(-60f, 0f, 0f);
                inst.transform.position = Vector3.zero;

                var lightGo = new GameObject("L"); var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                RenderSettings.ambientLight = new Color(0.4f, 0.4f, 0.45f);

                var camGo = new GameObject("C"); var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.14f, 0.14f, 0.16f);
                cam.transform.position = new Vector3(0f, 0.05f, -0.75f); // viewer side (-Z)
                cam.transform.LookAt(new Vector3(0f, 0f, 0.05f));
                cam.fieldOfView = 40f;

                var rt = new RenderTexture(900, 700, 24);
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(900, 700, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 900, 700), 0, 0); tex.Apply();
                File.WriteAllBytes(PreviewPng, tex.EncodeToPNG());
                RenderTexture.active = null; cam.targetTexture = null;
                Object.DestroyImmediate(inst); Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo);
                Debug.Log($"[GloomhavenVR] Preview render written: {Path.GetFullPath(PreviewPng)}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[GloomhavenVR] Preview render skipped ({e.GetType().Name}: {e.Message}).");
            }
        }
    }
}
