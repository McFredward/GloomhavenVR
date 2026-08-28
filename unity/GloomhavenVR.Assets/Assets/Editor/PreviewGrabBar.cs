// GloomhavenVR companion project — GRAB BAR verification renderer.
//
//   GRABBAR_PREVIEW_OUT=/somewhere xvfb-run -a \
//     Unity -batchmode -projectPath <this> -buildTarget Win64 \
//           -executeMethod GloomhavenVR.GrabBarPreview.RenderAll -logFile grabbar-preview.log -quit
//   (needs a graphics device — do NOT pass -nographics.)
//
// TOUCH THE SYMLINK FIRST. ALWAYS:
//
//   touch -h Assets/Editor/GrabBarMeshLink.cs
//
// UNITY DOES NOT REIMPORT A SYMLINKED SOURCE FILE when only its TARGET changes — the link's own
// mtime never moves, so the asset database sees nothing and rebuilds nothing, and the run compiles
// the PREVIOUS version of GrabBar.cs while reporting complete success. This cost two full rounds:
// a normals fix and a whole profile rework both landed in the source, were confirmed by the build,
// and did not appear in a single rendered pixel. The runs that DID change the picture had all
// touched a real file in this folder as well, which is what masked it.
//
// The tell, if it happens again: the [GrabBarPreview] MESH line prints the OLD vertex count and the
// OLD bounds. That line exists for this.
//
// WHY THIS EXISTS, and what makes it worth trusting.
//
// The four grab bars are built at RUNTIME by the plugin, not baked into the bundle, so nothing in
// this project would otherwise know what they look like. The obvious way to preview them is to
// re-type the lathe profile here — and that is exactly the trap: two copies of a mesh formula is
// how the picture you check stops being the picture you ship. So `GrabBarMeshLink.cs` in this
// folder is a SYMLINK to src/GloomhavenVR/Core/GrabBar.cs. The mesh drawn below is byte-for-byte
// the mesh the game builds, because it is the same file. (That is also why GrabBar.cs is the one
// file in the mod written against C# 9 with a block namespace: Unity 2021.3 cannot compile the
// file-scoped form the rest of src/ uses.)
//
// WHAT IT CANNOT SEE, stated so nobody trusts it further than it goes:
//
//  * THE TEXTURES ARE READ FROM src/GloomhavenVR/Assets/*.png, not from the plugin DLL. It
//    therefore proves the STRIPS are right and the UV mapping lands the cap bands on the caps. It
//    does NOT prove the csproj embeds them or that GrabBarTexture finds them at runtime — only a
//    rig log line ("Grab-bar strip 'Oak' decoded") proves that.
//  * THE WINDOW ROD'S SHADER IS UNLIT AND ITS ROUNDNESS IS BAKED INTO v. A render here shows
//    whether that bake reads as round. It cannot show whether the rod still sorts OVER a converted
//    menu canvas, because there is no canvas here — that is what _ZTest/_ZWrite are for and only
//    the rig can confirm it.
//  * PREVIEWS COME OUT BRIGHTER THAN THE HEADSET. Relative and structural claims ("the cap band
//    is on the cap", "Steel carries no gold") are settled here; absolute levels are not.
//
// Renders each of the four rods at the shipped geometry — radius 0.012 m, i.e. the 0.024 m
// thickness both bars have always had — at the board bar's length (BoardW 0.64 x 0.55 = 0.352 m)
// and again short, so the caps can be checked at both ends of the SetLength range.

using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class GrabBarPreview
    {
        // The one the plugin passes, so the station draws the rod the game builds.
        private static float Radius => GloomhavenVR.Core.GrabBarMesh.DefaultRadius;
        private const float BoardBarLength = 0.64f * 0.55f;
        private const float ShortBarLength = 0.12f;

        private static readonly string[] Styles = { "generic", "oak", "steel", "bronze" };

        private const int rtW = 1600;
        private const int rtH = 420;

        public static void RenderAll()
        {
            string outDir = System.Environment.GetEnvironmentVariable("GRABBAR_PREVIEW_OUT");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.GetFullPath("GrabBarPreview");
            Directory.CreateDirectory(outDir);
            Debug.Log("[GrabBarPreview] out = " + outDir);

            foreach (string style in Styles)
            {
                // The window rod is the unlit/Overlay one; the three board rods are lit.
                bool overlay = style == "generic";
                Shoot(style, overlay, BoardBarLength, "long", outDir);
                Shoot(style, overlay, ShortBarLength, "short", outDir);
            }
            Debug.Log("[GrabBarPreview] done.");
        }

        private static void Shoot(string style, bool overlay, float length, string tag, string outDir)
        {
            var root = new GameObject("GrabBarPreviewRoot");
            try
            {
                Material mat = BuildMaterial(style, overlay);
                Mesh shaft = GloomhavenVR.Core.GrabBarMesh.Shaft(Radius);
                Mesh cap = GloomhavenVR.Core.GrabBarMesh.Cap(Radius);
                // THE POSE COMES FROM THE SHARED FILE, not from a copy written here. The first
                // version of this preview carried its own copy of the placement and it was wrong in
                // exactly the same way the runtime's was — so the render agreed with the game and
                // neither caught it. A preview that duplicates the thing it is checking checks
                // nothing.
                float capLen = Radius * GloomhavenVR.Core.GrabBarMesh.CapLengthInRadii;
                GloomhavenVR.Core.GrabBarMesh.Pose(length, Radius, out float shaftLen,
                                                   out Vector3 leftPos, out Vector3 rightPos);

                Piece(root.transform, "Shaft", shaft, mat,
                      Vector3.zero, Quaternion.identity, new Vector3(shaftLen, 1f, 1f));
                Piece(root.transform, "CapA", cap, mat,
                      leftPos, Quaternion.identity, Vector3.one);
                Piece(root.transform, "CapB", cap, mat,
                      rightPos, GloomhavenVR.Core.GrabBarMesh.RightCapRotation, Vector3.one);

                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[GrabBarPreview] MESH shaft bounds c={0} s={1} verts={2} | cap bounds c={3} " +
                    "s={4} verts={5}",
                    shaft.bounds.center.ToString("F5"), shaft.bounds.size.ToString("F5"),
                    shaft.vertexCount,
                    cap.bounds.center.ToString("F5"), cap.bounds.size.ToString("F5"),
                    cap.vertexCount));
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[GrabBarPreview] {0}/{1}: length {2:F4} m, cap {3:F4} m each, shaft {4:F4} m, " +
                    "shader '{5}' (isSupported={6}), tex {7}",
                    style, tag, length, capLen, shaftLen, mat.shader != null ? mat.shader.name : "<null>",
                    mat.shader != null && mat.shader.isSupported,
                    mat.mainTexture != null
                        ? mat.mainTexture.width + "x" + mat.mainTexture.height
                        : "<none>"));

                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.46f);
                var lightGo = new GameObject("Key");
                Light key = lightGo.AddComponent<Light>();
                key.type = LightType.Directional;
                key.intensity = 1.05f;
                lightGo.transform.rotation = Quaternion.Euler(38f, -35f, 0f);

                var camGo = new GameObject("Cam");
                Camera cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.18f, 0.18f, 0.19f);
                cam.orthographic = false;
                cam.fieldOfView = 24f;
                // NEAR CLIP, and it is not housekeeping. Framing on a 0.35 m rod puts the camera
                // ~0.24 m away, INSIDE Unity's default 0.3 m near plane — the first reframed run
                // rendered four perfectly clean, perfectly EMPTY frames and reported success. A
                // preview station will always agree with you; this one has to be able to see.
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 20f;
                // Three-quarter view from above, FRAMED ON THE ROD rather than at an arbitrary
                // distance. The first cut of this file put the camera at 2.1x the length and the
                // rod came out a third of the frame wide — too small to judge a silhouette from,
                // which is the only thing this station exists to judge. Solve the distance from the
                // horizontal half-angle instead, and leave 12 % margin.
                float hFov = 2f * Mathf.Atan(Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)
                                             * ((float)rtW / rtH));
                float dist = (length * 1.34f * 0.5f) / Mathf.Tan(hFov * 0.5f);
                // A THREE-QUARTER HERO ANGLE, not a side elevation — the same view the design
                // sheets were drawn at. Judging a flat side-on render against a three-quarter
                // reference compares two different pictures and flatters neither: foreshortening
                // is what shows a rod's taper, and an oblique view is the only one where the
                // knob's ball reads as a ball.
                camGo.transform.position = new Vector3(dist * 0.42f, dist * 0.30f, -dist * 0.86f);
                camGo.transform.LookAt(Vector3.zero);

                var rt = new RenderTexture(rtW, rtH, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                shot.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;

                File.WriteAllBytes(Path.Combine(outDir, style + "_" + tag + ".png"),
                                   shot.EncodeToPNG());

                Object.DestroyImmediate(shot);
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(lightGo);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Piece(Transform parent, string name, Mesh mesh, Material mat,
                                  Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>Bind one map off src/GloomhavenVR/Assets — the same PNG the plugin embeds.
        /// Returns false, loudly, when the file is not there, so a missing map is a log line rather
        /// than a silently matte rod.</summary>
        private static bool Bind(Material mat, string property, string style, string suffix,
                                 bool linear)
        {
            if (!mat.HasProperty(property))
                return false;
            string png = Path.GetFullPath(Path.Combine(
                "..", "..", "src", "GloomhavenVR", "Assets",
                "grabbar_" + style + suffix + ".png"));
            if (!File.Exists(png))
            {
                Debug.LogError("[GrabBarPreview] map missing: " + png);
                return false;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            if (!tex.LoadImage(File.ReadAllBytes(png)))
            {
                Debug.LogError("[GrabBarPreview] could not decode " + png);
                return false;
            }
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 4;
            tex.Apply(true);
            mat.SetTexture(property, tex);
            return true;
        }

        private static Material BuildMaterial(string style, bool overlay)
        {
            // Same two shaders the runtime picks, resolved through AssetDatabase so they compile
            // for THIS editor's graphics API (the bundle's D3D11-only variants would draw magenta
            // here — see PreviewBoard.cs, which documents that trap at length).
            string shaderPath = overlay
                ? "Assets/Bundle/Table/Overlay.shader"
                : "Assets/Bundle/Table/BoardLit.shader";
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
            {
                Debug.LogError("[GrabBarPreview] shader not found: " + shaderPath);
                shader = Shader.Find("Standard");
            }

            var mat = new Material(shader) { color = Color.white };

            Bind(mat, "_MainTex", style, "", linear: false);
            if (!overlay)
            {
                // The three LIT rods. Without these two the shader's specular block never runs
                // (_SpecStrength defaults to 0) and the render comes out matte — which is what the
                // first pass shipped, and most of why the rods looked nothing like the design sheet.
                if (Bind(mat, "_BumpMap", style, "_n", linear: true)
                    && mat.HasProperty("_NormalStrength"))
                    mat.SetFloat("_NormalStrength", GloomhavenVR.Core.GrabBarMesh.NormalStrength);
                if (Bind(mat, "_MRSMap", style, "_mrs", linear: true)
                    && mat.HasProperty("_SpecStrength"))
                    mat.SetFloat("_SpecStrength", GloomhavenVR.Core.GrabBarMesh.SpecStrength);
            }

            if (overlay && mat.HasProperty("_ZWrite"))
                mat.SetInt("_ZWrite", 1);
            return mat;
        }
    }
}
