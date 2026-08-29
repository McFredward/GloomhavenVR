// GloomhavenVR companion project — VR controller prefab builder.
//
// Menu:  GloomhavenVR > Build Controller Prefabs
// Batch: Unity.exe -batchmode -nographics -projectPath <this project> -buildTarget Win64
//        -executeMethod GloomhavenVR.ControllersBuilder.BuildAll -logFile build-controllers.log
//
// INPUT is whatever controllers_pipeline.py wrote under Assets/Bundle/Controllers/<id>/:
// one OBJ per (key, material), one base-colour texture per material, and a controller.json
// naming the parts, the materials and each key's ANCHOR. OUTPUT is one prefab per device and
// hand, with ONE CHILD PER KEY — which is the entire reason the meshes were split: the
// tutorial's "this key, now" highlight is then a material swap on that child's renderer, with
// no submesh arithmetic and no per-device special case.
//
// THE IMPORTER IS CHECKED, NOT TRUSTED, AND IT DID NOT PASS. Unity's OBJ importer treats OBJ
// as right-handed and NEGATES X on the way in. That is a bug that looks almost right -- every
// key sits in a believable place, on the wrong side of the controller -- and the first run of
// this builder caught it on generic_left_body (min.x -0.0284 where the pipeline wrote -0.0366,
// which is exactly -max.x). The pipeline therefore records each mesh's bounds and signed volume
// in the manifest, and RestoreImporterFlip below undoes the mirroring, deciding from the
// imported mesh's own signed volume whether the importer also reversed the winding. Nothing
// here is assumed: the corrected mesh is checked against the manifest before it is saved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class ControllersBuilder
    {
        private const string Root = "Assets/Bundle/Controllers";
        private const string ShaderName = "GloomhavenVR/BoardLit";

        /// <summary>Bounds agreement in metres. The OBJ round-trip is float text at six
        /// decimals, so this is loose enough for the format and far tighter than any axis
        /// flip, which would move a bound by centimetres.</summary>
        private const float BoundsTolerance = 0.0005f;

        [Serializable] private sealed class MeshEntry
        {
            public string part; public int material; public string mesh;
            public int vertices; public int triangles;
            public float[] min; public float[] max; public float signedVolume;
        }

        [Serializable] private sealed class MaterialEntry
        {
            public int index; public string name; public float[] baseColorFactor;
            public float metallic; public float roughness; public string texture;
        }

        [Serializable] private sealed class AnchorEntry
        {
            public string part; public float[] position;
        }

        [Serializable] private sealed class HandEntry
        {
            public List<MeshEntry> meshes; public List<MaterialEntry> materials;
            public List<AnchorEntry> anchors;
        }

        [Serializable] private sealed class Manifest
        {
            public string modId; public string profile; public string source;
            public HandEntry left; public HandEntry right;
        }

        [MenuItem("GloomhavenVR/Build Controller Prefabs")]
        public static void BuildFromMenu() => Build();

        public static void BuildAll()
        {
            try
            {
                Build();
                Debug.Log("[GloomhavenVR] BuildControllers OK");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GloomhavenVR] BuildControllers FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static void Build()
        {
            string[] dirs = Directory.GetDirectories(Root).OrderBy(d => d).ToArray();
            if (dirs.Length == 0)
                throw new Exception($"No device folders under {Root} — run controllers_pipeline.py first.");

            int built = 0;
            foreach (string dir in dirs)
            {
                string json = Path.Combine(dir, "controller.json").Replace('\\', '/');
                if (!File.Exists(json))
                    continue;
                var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(json));
                if (manifest == null || string.IsNullOrEmpty(manifest.modId))
                    throw new Exception($"{json} did not parse into a manifest.");

                var materials = new Dictionary<int, Material>();
                foreach (string hand in new[] { "left", "right" })
                {
                    HandEntry entry = hand == "left" ? manifest.left : manifest.right;
                    if (entry == null || entry.meshes == null || entry.meshes.Count == 0)
                        throw new Exception($"{json}: no meshes for the {hand} hand.");
                    BuildHand(dir, manifest, hand, entry, materials);
                    built++;
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[GloomhavenVR] {built} controller prefab(s) written under {Root}.");
        }

        private static void BuildHand(string dir, Manifest manifest, string hand,
                                      HandEntry entry, Dictionary<int, Material> materials)
        {
            string id = manifest.modId;
            var root = new GameObject($"Controller_{id}_{hand}");
            var byPart = new Dictionary<string, Transform>(StringComparer.Ordinal);

            foreach (MeshEntry me in entry.meshes)
            {
                string objPath = $"{dir}/{me.mesh}.obj".Replace('\\', '/');
                ConfigureModelImport(objPath);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(objPath)
                    ?? throw new Exception($"{objPath} did not import as a model.");
                Mesh imported = source.GetComponentsInChildren<MeshFilter>()
                                  .Select(f => f.sharedMesh).FirstOrDefault(m => m != null)
                    ?? throw new Exception($"{objPath} imported without a mesh.");

                Mesh mesh = RestoreImporterFlip(dir, objPath, imported, me);
                VerifyBounds(objPath, mesh, me);

                Transform part = GetOrCreatePart(root.transform, byPart, me.part);
                var go = new GameObject(me.mesh);
                go.transform.SetParent(part, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial =
                    GetMaterial(dir, id, entry, me.material, materials);
            }

            // Anchors last, so a key that has no mesh of its own (the Index's grip is a force
            // sensor in the handle, and moves nothing) still gets a part transform the runtime
            // can put a marker on.
            foreach (AnchorEntry ae in entry.anchors ?? new List<AnchorEntry>())
            {
                if (ae.position == null || ae.position.Length != 3)
                    continue;
                Transform part = GetOrCreatePart(root.transform, byPart, ae.part);
                var anchor = new GameObject("Anchor");
                anchor.transform.SetParent(part, false);
                anchor.transform.localPosition =
                    new Vector3(ae.position[0], ae.position[1], ae.position[2]);
            }

            string prefabPath = $"{dir}/Controller_{id}_{hand}.prefab".Replace('\\', '/');
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log($"[GloomhavenVR] {prefabPath}: {byPart.Count} key part(s) — "
                      + string.Join(", ", byPart.Keys.OrderBy(k => k)));
        }

        private static Transform GetOrCreatePart(Transform root,
            Dictionary<string, Transform> byPart, string part)
        {
            if (byPart.TryGetValue(part, out Transform existing))
                return existing;
            var go = new GameObject(part);
            go.transform.SetParent(root, false);
            byPart[part] = go.transform;
            return go.transform;
        }

        /// <summary>
        /// Undo the X mirroring Unity's OBJ importer applies, and save the corrected mesh as its
        /// own asset so the prefab references geometry that matches the manifest exactly.
        ///
        /// <para>WHETHER THE IMPORTER ALSO REVERSED THE WINDING IS MEASURED, NOT ASSUMED. Negating
        /// X alone flips the sign of a mesh's signed volume; negating X and reversing the winding
        /// leaves it alone. So comparing the imported volume's sign against the one the pipeline
        /// recorded says which of the two happened, and the correction is chosen from that. Either
        /// way the mesh that leaves this method has the manifest's bounds AND its signed volume,
        /// and <see cref="VerifyBounds"/> then says so out loud.</para>
        /// </summary>
        private static Mesh RestoreImporterFlip(string dir, string objPath, Mesh imported,
                                                MeshEntry me)
        {
            Vector3[] verts = imported.vertices;
            Vector3[] normals = imported.normals;
            Vector2[] uvs = imported.uv;
            int[] tris = imported.triangles;

            float importedVolume = SignedVolume(verts, tris);
            bool windingKept = Mathf.Sign(importedVolume) == Mathf.Sign(me.signedVolume);

            for (int i = 0; i < verts.Length; i++)
                verts[i].x = -verts[i].x;
            if (normals != null)
                for (int i = 0; i < normals.Length; i++)
                    normals[i].x = -normals[i].x;
            // Negating X flips handedness once. If the importer had preserved the winding
            // relative to the source (volume sign kept), it must be reversed here to cancel this
            // flip; if the importer had already reversed it (volume sign flipped), it must be
            // left alone, because that reversal is the one being undone.
            if (windingKept)
                for (int i = 0; i < tris.Length; i += 3)
                    (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);

            var mesh = new Mesh { name = me.mesh };
            if (verts.Length > 65534)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            if (uvs != null && uvs.Length == verts.Length) mesh.uv = uvs;
            if (normals != null && normals.Length == verts.Length) mesh.normals = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            string meshPath = $"{dir}/{me.mesh}.asset".Replace('\\', '/');
            AssetDatabase.CreateAsset(mesh, meshPath);
            return mesh;
        }

        private static float SignedVolume(Vector3[] v, int[] tris)
        {
            double sum = 0.0;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
                sum += Vector3.Dot(a, Vector3.Cross(b, c));
            }
            return (float)(sum / 6.0);
        }

        /// <summary>The check that makes the importer's behaviour a measurement instead of an
        /// assumption: bounds catch a mirrored axis, signed volume catches an inverted winding,
        /// and neither can be talked out of by the other.</summary>
        private static void VerifyBounds(string objPath, Mesh mesh, MeshEntry me)
        {
            if (me.min == null || me.max == null || me.min.Length != 3 || me.max.Length != 3)
                throw new Exception($"{objPath}: the manifest carries no bounds to check against.");
            Bounds b = mesh.bounds;
            var wantMin = new Vector3(me.min[0], me.min[1], me.min[2]);
            var wantMax = new Vector3(me.max[0], me.max[1], me.max[2]);
            float dmin = Vector3.Distance(b.min, wantMin);
            float dmax = Vector3.Distance(b.max, wantMax);
            if (dmin > BoundsTolerance || dmax > BoundsTolerance)
                throw new Exception(
                    $"{objPath}: Unity imported this mesh with DIFFERENT bounds than the pipeline "
                    + $"wrote — min {b.min:F4} vs {wantMin:F4} (Δ{dmin:F4} m), "
                    + $"max {b.max:F4} vs {wantMax:F4} (Δ{dmax:F4} m). The OBJ importer has "
                    + "transformed the mesh (an axis flip or a scale factor); fix the import "
                    + "settings rather than shipping a mirrored controller.");
            if (mesh.triangles.Length / 3 != me.triangles)
                throw new Exception($"{objPath}: {mesh.triangles.Length / 3} triangles imported, "
                    + $"{me.triangles} written.");
            float volume = SignedVolume(mesh.vertices, mesh.triangles);
            float wantVolume = me.signedVolume;
            if (Mathf.Sign(volume) != Mathf.Sign(wantVolume)
                || Mathf.Abs(volume - wantVolume) > 1e-4f * Mathf.Max(Mathf.Abs(wantVolume), 1e-9f))
                throw new Exception(
                    $"{objPath}: signed volume {volume:E4} m3 does not match the pipeline's "
                    + $"{wantVolume:E4} m3 — the winding is inverted, so this mesh would render "
                    + "inside-out under Cull Back.");
        }

        private static void ConfigureModelImport(string objPath)
        {
            var importer = AssetImporter.GetAtPath(objPath) as ModelImporter;
            if (importer == null)
                throw new Exception($"{objPath} has no ModelImporter (did it import at all?).");
            bool dirty = false;
            // useFileScale off + globalScale 1 keeps the metres the pipeline wrote. The OBJ
            // carries authored normals, so importing them beats recalculating (the smoothing
            // groups the source glTF had are already baked into them).
            if (importer.useFileScale) { importer.useFileScale = false; dirty = true; }
            if (!Mathf.Approximately(importer.globalScale, 1f)) { importer.globalScale = 1f; dirty = true; }
            if (importer.importNormals != ModelImporterNormals.Import)
            { importer.importNormals = ModelImporterNormals.Import; dirty = true; }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            { importer.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            if (importer.importAnimation) { importer.importAnimation = false; dirty = true; }
            // Readable: RestoreImporterFlip reads the vertices back at build time.
            if (!importer.isReadable) { importer.isReadable = true; dirty = true; }
            if (dirty)
                importer.SaveAndReimport();
        }

        private static Material GetMaterial(string dir, string id, HandEntry entry,
                                            int index, Dictionary<int, Material> cache)
        {
            if (cache.TryGetValue(index, out Material cached))
                return cached;

            Shader shader = Shader.Find(ShaderName)
                ?? throw new Exception($"Bundled shader '{ShaderName}' not found (compile error?).");
            MaterialEntry me = entry.materials?.FirstOrDefault(m => m.index == index);
            var mat = new Material(shader) { name = $"Controller_{id}_m{index}" };
            if (me?.baseColorFactor != null && me.baseColorFactor.Length == 4)
                mat.color = new Color(me.baseColorFactor[0], me.baseColorFactor[1],
                                      me.baseColorFactor[2], me.baseColorFactor[3]);
            if (!string.IsNullOrEmpty(me?.texture))
            {
                string texPath = $"{dir}/{me.texture}".Replace('\\', '/');
                ConfigureAlbedoImport(texPath);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex != null)
                    mat.SetTexture("_MainTex", tex);
                else
                    Debug.LogWarning($"[GloomhavenVR] {texPath} missing — {id} renders as a flat tint.");
            }
            mat.SetFloat("_Cull", 2f); // Back: the meshes are closed and correctly wound.
            string matPath = $"{dir}/Controller_{id}_m{index}.mat".Replace('\\', '/');
            AssetDatabase.CreateAsset(mat, matPath);
            cache[index] = mat;
            return mat;
        }

        private static void ConfigureAlbedoImport(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
                return;
            bool dirty = false;
            if (ti.textureType != TextureImporterType.Default)
            { ti.textureType = TextureImporterType.Default; dirty = true; }
            if (!ti.sRGBTexture) { ti.sRGBTexture = true; dirty = true; }
            if (ti.maxTextureSize > 1024) { ti.maxTextureSize = 1024; dirty = true; }
            if (dirty)
                ti.SaveAndReimport();
        }
    }
}
