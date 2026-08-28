// GloomhavenVR companion project — the README's BOARD TILES, with the grab rod attached.
//
//   BOARD_ASSET_OUT=unity/asset-preview/render xvfb-run -a \
//     Unity -batchmode -projectPath <this> -buildTarget Win64 \
//           -executeMethod GloomhavenVR.BoardAssetShot.RenderAll -logFile board-asset.log -quit
//   (needs a graphics device — do NOT pass -nographics.)
//   Then: touch -h Assets/Editor/GrabBarMeshLink.cs first if GrabBar.cs changed — Unity does not
//   reimport a symlinked source when only its target moves.
//
// WHY THIS EXISTS.
//
// The README's styles matrix showed the three boards and, for one build, the three rods as a
// SEPARATE ROW underneath. The user's correction: "Statt eine eigene Zeile render die Boards
// direkt mit dem Greifbalken an der Stelle an der man es auch sieht im Spiel." A rod in its own
// row makes the reader pair it with a board; a rod bolted to the board's own bottom edge does not.
//
// WHY UNITY AND NOT unity/asset-preview/render_asset.py, WHICH RENDERS EVERY OTHER TILE.
// That script is Blender, and it takes FBX + albedo. It cannot draw this rod at all: the rod is a
// PROCEDURAL mesh built in C# at runtime, so there is no FBX to hand it. It would have needed a
// mesh exporter and a second-object mode.
//
// And the swap is an upgrade rather than a compromise. render_asset.py's own header says it
// REPRODUCES BoardLit's arithmetic — "shade = 0.5 + saturate(N.key) * 0.85 + saturate(N.fill) *
// 0.35" — because Blender has no way to run the shader. This project HAS the shader: the prefab is
// loaded through AssetDatabase, so BoardLit compiles for this editor's own graphics API and draws
// the board the way the game's material actually draws it, specular lobe and all. A reproduction
// of an arithmetic is a very good approximation; the arithmetic itself is not an approximation.
//
// WHAT IS MATCHED TO THE BLENDER TILES ON PURPOSE, so the matrix's three rows still read as one
// picture: 900 px square, ORTHOGRAPHIC at 0.78 m across (build_asset_strips.sh's --scale 0.78),
// yaw 35, pitch 50, and a TRANSPARENT clear — styles_matrix composites transparent renders onto
// white, so an opaque ground would arrive as a rectangle. The pitch is the user's own from that
// script: "PITCH 18 IS EDGE-ON. A tray lies FLAT ... the player looks down at it, so 50 does what
// his eye does."
//
// WHERE THE ROD SITS is not a composition choice. It is PlayTray.BuildHandle's own mount,
// (0, -BoardH/2 - 0.030, +0.004) board-local, at BoardW * 0.55 long — the same three numbers the
// game uses, read off the same constants, so the picture cannot drift from the product.

using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class BoardAssetShot
    {
        // PlayTray's own board metrics. The preview station measures the shipped prefab at
        // 0.64 x 0.32 m and logs it every run; these are those numbers, and BuildHandle's mount is
        // derived from them exactly as the game derives it.
        private const float BoardW = 0.64f;
        private const float BoardH = 0.32f;
        private const float RodLength = BoardW * 0.55f;
        private static readonly Vector3 RodMount = new(0f, -BoardH * 0.5f - 0.030f, 0.004f);

        // Matched to unity/asset-preview/build_asset_strips.sh's board calls.
        private const int Res = 900;
        private const float OrthoWidth = 0.78f;
        private const float Yaw = 35f;
        private const float Pitch = 50f;

        // The rod is named by its TEXTURE STEM, not by Core.GrabBarStyle: only GrabBar.cs is
        // symlinked into this project (it is the file that must not diverge), and the enum lives in
        // GrabBarTexture.cs, which pulls in VRLog and Cards.PlayTray and has no business here.
        // PreviewGrabBar.cs names its rods the same way for the same reason.
        private static readonly (string Out, string Prefab, string Rod)[] Boards =
        {
            ("board_oak",    "Assets/Bundle/Table/PlayTray.prefab",           "oak"),
            ("board_steel",  "Assets/Bundle/Table/PlayTray_9capjqp6.prefab",  "steel"),
            ("board_bronze", "Assets/Bundle/Table/PlayTray_16vm268h.prefab",  "bronze"),
        };

        public static void RenderAll()
        {
            string outDir = System.Environment.GetEnvironmentVariable("BOARD_ASSET_OUT");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.GetFullPath(Path.Combine("..", "asset-preview", "render"));
            Directory.CreateDirectory(outDir);
            Debug.Log("[BoardAssetShot] out = " + outDir);

            foreach (var (file, prefabPath, rodStyle) in Boards)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogError("[BoardAssetShot] prefab not found: " + prefabPath);
                    continue;
                }
                Shoot(file, prefab, rodStyle, outDir);
            }
            Debug.Log("[BoardAssetShot] done.");
        }

        private static void Shoot(string file, GameObject prefab, string rodStyle,
                                  string outDir)
        {
            GameObject board = Object.Instantiate(prefab);
            var camGo = new GameObject("Cam");
            var rt = new RenderTexture(Res, Res, 24, RenderTextureFormat.ARGB32);
            try
            {
                board.transform.position = Vector3.zero;
                // LAY THE BOARD FLAT, because that is the pose the other tiles were shot in.
                // PlayTray is a VERTICAL panel in the game — it faces the player — so a 50 degree
                // down-look sees it nearly edge-on. render_asset.py's boards arrive from FBX lying
                // flat, which is what its own note is about: "PITCH 18 IS EDGE-ON. A tray lies
                // FLAT. Shot from 18 degrees it is a sliver; the player looks down at it, so 50
                // does what his eye does." Rotating -90 about X turns the board's face (+Z) up to
                // +Y and puts it in that same frame. The rod is a CHILD, so it follows.
                // (-90 lays it face DOWN and renders the plain back of the tray — checked.)
                board.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                AttachRod(board.transform, rodStyle);

                Camera cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // the matrix composites on white
                cam.orthographic = true;
                cam.orthographicSize = OrthoWidth * 0.5f;          // square render: half the width
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 20f;
                // THE CAMERA POSE IS TAKEN FROM render_asset.py, NOT GUESSED AT.
                //
                // The first cut wrote Quaternion.Euler(pitch, -yaw, 0) and the boards came out
                // visibly more isometric than the shipped tiles — a different photograph of the
                // same object, which in a matrix reads as the rows disagreeing about what a board
                // looks like. Euler angles are a convention, and Blender's is not Unity's.
                //
                // That script builds a DIRECTION and puts the camera on it:
                //
                //     d = ( sin(yaw)*cos(pitch), -cos(yaw)*cos(pitch), sin(pitch) )   [Blender]
                //
                // in Blender's Z-up right-handed frame. Its own header gives the mapping —
                // "(x, y, z)_unity -> (x, z, y)_blender" — so the same direction in Unity is
                //
                //     d = ( sin(yaw)*cos(pitch), sin(pitch), -cos(yaw)*cos(pitch) )
                //
                // which at yaw 35 / pitch 50 is (0.369, 0.766, -0.527). Placing the camera along it
                // and looking back gives the same photograph, with no convention left to get wrong.
                float y = Yaw * Mathf.Deg2Rad, pi = Pitch * Mathf.Deg2Rad;
                // The yaw is NEGATED across the two engines. Renaming axes is not enough: Blender
                // is right-handed and Unity is left-handed, so the same formula walks the camera
                // around the object the other way. Without the flip the board's long axis ran
                // upper-left to lower-right where the shipped tiles run lower-left to upper-right —
                // a mirrored photograph of the same object.
                var dir = new Vector3(-Mathf.Sin(y) * Mathf.Cos(pi),
                                      Mathf.Sin(pi),
                                      -Mathf.Cos(y) * Mathf.Cos(pi)).normalized;

                // FRAME ON WHAT IS DRAWN, board AND rod together. The Blender script auto-fits to
                // the object it was handed; the rod hangs 204 mm below the board's centre, so
                // aiming at the board's own centre would push it toward the bottom edge and, at a
                // pinned ortho width, off it.
                Bounds b = Content(board);
                camGo.transform.position = b.center + dir * 2f;
                camGo.transform.LookAt(b.center);

                // BoardLit bakes its own two light directions, so the scene's lighting has no say
                // here — which is the point. An ambient floor is set anyway for anything on the
                // prefab that is NOT BoardLit.
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);

                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var shot = new Texture2D(Res, Res, TextureFormat.RGBA32, false);
                shot.ReadPixels(new Rect(0, 0, Res, Res), 0, 0);
                shot.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;

                File.WriteAllBytes(Path.Combine(outDir, file + ".png"), shot.EncodeToPNG());
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[BoardAssetShot] {0}: rod {1} at {2}, length {3:F4} m, ortho {4:F3} m",
                    file, rodStyle, RodMount.ToString("F4"), RodLength, OrthoWidth));
                Object.DestroyImmediate(shot);
            }
            finally
            {
                RenderTexture.active = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(board);
            }
        }

        /// <summary>
        /// The rod, built from the SAME shared source the plugin runs (Assets/Editor is symlinked
        /// to src/GloomhavenVR/Core/GrabBar.cs) and posed with the same helper, so what the README
        /// shows is what the game builds.
        /// </summary>
        private static void AttachRod(Transform board, string style)
        {
            var root = new GameObject("GrabRod").transform;
            root.SetParent(board, worldPositionStays: false);
            root.localPosition = RodMount;

            float radius = Core.GrabBarMesh.DefaultRadius;
            float tiles = Core.GrabBarMesh.QuantiseTiles(
                (RodLength - 2f * radius * Core.GrabBarMesh.CapLengthInRadii)
                / Core.GrabBarMesh.TileLength);
            Mesh shaft = Core.GrabBarMesh.Shaft(radius, tiles);
            Mesh cap = Core.GrabBarMesh.Cap(radius);
            Core.GrabBarMesh.Pose(RodLength, radius, out float shaftLen,
                                  out Vector3 leftPos, out Vector3 rightPos);

            Material mat = RodMaterial(style);
            Piece(root, "Shaft", shaft, mat, Vector3.zero, Quaternion.identity,
                  new Vector3(shaftLen, 1f, 1f));
            Piece(root, "CapA", cap, mat, leftPos, Quaternion.identity, Vector3.one);
            Piece(root, "CapB", cap, mat, rightPos, Core.GrabBarMesh.RightCapRotation, Vector3.one);
        }

        /// <summary>The union of every enabled renderer under <paramref name="root"/> — what the
        /// picture actually contains, which is the board plus its rod.</summary>
        private static Bounds Content(GameObject root)
        {
            var rs = root.GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0)
                return new Bounds(root.transform.position, Vector3.one * 0.1f);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++)
                b.Encapsulate(rs[i].bounds);
            return b;
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

        private static Material RodMaterial(string style)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Bundle/Table/BoardLit.shader");
            var mat = new Material(shader != null ? shader : Shader.Find("Standard"))
            { color = Color.white };
            string stem = "grabbar_" + style;
            Bind(mat, "_MainTex", stem, "", linear: false);
            if (Bind(mat, "_BumpMap", stem, "_n", linear: true) && mat.HasProperty("_NormalStrength"))
                mat.SetFloat("_NormalStrength", Core.GrabBarMesh.NormalStrength);
            if (Bind(mat, "_MRSMap", stem, "_mrs", linear: true) && mat.HasProperty("_SpecStrength"))
                mat.SetFloat("_SpecStrength", Core.GrabBarMesh.SpecStrength);
            return mat;
        }

        private static bool Bind(Material mat, string property, string stem, string suffix,
                                 bool linear)
        {
            if (!mat.HasProperty(property))
                return false;
            string png = Path.GetFullPath(Path.Combine(
                "..", "..", "src", "GloomhavenVR", "Assets", stem + suffix + ".png"));
            if (!File.Exists(png))
            {
                Debug.LogError("[BoardAssetShot] map missing: " + png);
                return false;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            if (!tex.LoadImage(File.ReadAllBytes(png)))
            {
                Debug.LogError("[BoardAssetShot] could not decode " + png);
                return false;
            }
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 4;
            tex.Apply(true);
            mat.SetTexture(property, tex);
            return true;
        }
    }
}
