// GloomhavenVR companion project — control-board VERIFICATION renderer.
//
// Batch:
//   BOARD_PREVIEW_OUT=/somewhere xvfb-run -a \
//     Unity -batchmode -projectPath <this> -buildTarget Win64 \
//           -executeMethod GloomhavenVR.BoardPreview.RenderAll -logFile board-preview.log
//   (needs a graphics device — do NOT pass -nographics.)
//
// WHY THIS EXISTS, and why it does not read Assets/.
//
// The board rebuild was judged for four rounds off unity/board-prep renders. Those are
// Blender/Cycles renders of the FBX with a Principled BSDF that binds `<base>_mr.png`
// and drives Metallic and Roughness from it. The SHIPPED material is BoardLit with
// `_MRSMap` unbound and `_SpecStrength = 0`, so every metallic highlight in those
// renders is a term the game never evaluates. A render of the source assets through a
// different shader is a picture of something that is not shipped.
//
// So this renderer opens `Build/Bundles/gloomhavenvr.bundle` — the artifact that goes
// on the rig — with AssetBundle.LoadFromFile, at the exact asset paths
// VRCardFactory uses, and instantiates the prefab that comes OUT of it. Whatever the
// bundle actually contains is what gets drawn, through whatever material actually
// travelled with the prefab. If the bundle is broken, this fails; if the material lost
// a texture, the picture shows it.
//
// It also DUMPS THE MEASUREMENTS at full precision. BoardBuilder logs anchor positions
// through Vector3.ToString(), which is two decimals — "0.00" there is anything inside
// ±5 mm, which is the same order as the quantities being checked. Everything printed
// below is F5 (10 µm), read back off the built prefab.
//
// WHAT THIS RENDERER CANNOT SEE, stated so nobody trusts it further than it goes.
// The bundle is compiled for StandaloneWindows64, so every shader inside carries D3D11
// subprograms and nothing else. Opening it in a Linux editor drawing through OpenGL
// finds no compatible variant: the Shader object is there and reports its NAME, but it
// has no program and no property list, so `HasProperty("_MainTex")` is false and the
// board draws MAGENTA. That is a platform artifact of the viewer, not a defect in the
// bundle — first observed 2026-08-25, and it looks exactly like the real pink-material
// trap, which is why it is written down here.
//
// So the run does two passes and they answer different questions:
//   BUNDLE pass  — is the shipped artifact structurally right? Prefabs present at the
//                  paths the mod asks for, anchors, seat/rest extents, collider, and
//                  the material carrying a shader of the right NAME. No picture.
//   PROJECT pass — what does the material LOOK like? Same prefab asset, same material
//                  asset, but resolved through AssetDatabase so the shader compiles for
//                  the editor's own API. This is the picture to judge.
// The two passes cross-check each other: every measurement is printed from both, and a
// disagreement means the bundle did not get what the project has.
// Neither pass can prove the D3D11 shader variants are good. Only the rig can.

using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class BoardPreview
    {
        private const string BundlePath = "Build/Bundles/gloomhavenvr.bundle";

        // The three asset paths as the MOD asks for them (VRCardFactory.SteelTrayPath /
        // BronzeTrayPath and the unsuffixed oak one). Named by the user-facing style so
        // the log and the PNGs are readable.
        private static readonly (string Style, string Path)[] BoardAssets =
        {
            ("oak",    "Assets/Bundle/Table/PlayTray.prefab"),
            ("steel",  "Assets/Bundle/Table/PlayTray_9capjqp6.prefab"),
            ("bronze", "Assets/Bundle/Table/PlayTray_16vm268h.prefab"),
        };

        private static readonly string[] AnchorNames =
        {
            "Slot1", "Slot2", "ShortRestToken", "LongRestToken",
            "ButtonSeat1", "ButtonSeat2", "ButtonSeat3",
            "ConfirmButton", "UndoButton", "SkipButton",
        };

        private static readonly string[] ExtentNames =
        {
            "SeatExtent1", "SeatExtent2", "SeatExtent3", "RestExtentShort", "RestExtentLong",
        };

        // ---- THE USER'S OWN ASSET-POSE DIALS, replayed against the re-authored mesh ----------
        //
        // `[Cards] AssetOffset_{board}` and `AssetPitch/Yaw/RollDegrees_{board}` move the BOARD
        // MESH under anchors that stay pinned where they were (Cards/PlayTray.3.Pose.SetAssetPose).
        // They exist to correct a mesh whose decorated face is not coplanar with its own anchor
        // plane, and the shipped Bronze board WAS such a mesh — a raked lectern 301 mm deep. The
        // user dialled -110 mm / +80 mm and 57 deg of pitch to lay it flat, and those numbers are
        // in his live cfg, where they beat any shipped default this repo can change.
        //
        // The re-authored Bronze is a flat 0.640 x 0.320 x 0.0354 m plate whose face IS the anchor
        // plane, so the same dials do not correct anything — they tip the mesh out from under a
        // control set that does not follow it. That was an INFERENCE from reading SetAssetPose;
        // this shot makes it an observation, and it is also how the fix gets checked, because a
        // remedy for a defect nobody has photographed is a remedy nobody can falsify.
        //
        // Values verified 2026-08-25 against .planning/debug/default/dev.gloomhavenvr.cards.cfg
        // lines 931/941 (Bronze) and 463/473, 697/707 (Oak, Steel — both identity, so their shot
        // is a control that must come out identical to the plain one).
        // The `clamped` row is what Cards.BoardAnchors.ClampAssetPose leaves of the row above it,
        // recomputed here BY HAND from the same arithmetic — a deliberate copy, the same kind the
        // alias table in BuildBoard.cs is, and for the same reason: this file compiles into the
        // companion project and cannot reference the mod. It is a CROSS-CHECK, not a second
        // implementation: if the mod's clamp and this hand-computation disagree, the numbers below
        // stop matching the mod's own "Board mesh pose CLAMPED" log line and one of the two is
        // wrong. Bronze's furthest pinned anchor is 0.2323 m from the root, the lift budget is
        // 5 mm split half to the offset and half to the tilt, so the offset clamps to 2.5 mm per
        // axis and the tilt to asin(0.0025/0.2323) = 0.617 deg.
        private static readonly (string Style, string Tag, Vector3 Offset, Vector3 Euler)[] UserAssetPose =
        {
            ("oak",    "userpose", new Vector3(0f, 0f, 0f),          new Vector3(0f, 0f, 0f)),
            ("steel",  "userpose", new Vector3(0f, 0f, 0f),          new Vector3(0f, 0f, 0f)),
            ("bronze", "userpose", new Vector3(0f, -0.11f, 0.08f),   new Vector3(57f, 0f, 0f)),
            ("bronze", "clamped",  new Vector3(0f, -0.0025f, 0.0025f), new Vector3(0.617f, 0f, 0f)),
        };

        public static void RenderAll()
        {
            int exit = 0;
            try
            {
                string outDir = System.Environment.GetEnvironmentVariable("BOARD_PREVIEW_OUT");
                if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath("board-preview");
                Directory.CreateDirectory(outDir);

                string bundleFull = Path.GetFullPath(BundlePath);
                if (!File.Exists(bundleFull))
                    throw new FileNotFoundException($"No bundle at {bundleFull} — build it first.");
                Debug.Log($"[BoardPreview] bundle: {bundleFull}  {new FileInfo(bundleFull).Length} bytes");

                var bundle = AssetBundle.LoadFromFile(bundleFull);
                if (bundle == null)
                    throw new System.Exception($"AssetBundle.LoadFromFile returned null for {bundleFull} "
                                               + "— the bundle is unreadable by this runtime.");
                Debug.Log($"[BoardPreview] bundle opened, {bundle.GetAllAssetNames().Length} asset name(s).");

                foreach (var (style, path) in BoardAssets)
                {
                    var fromBundle = bundle.LoadAsset<GameObject>(path);
                    if (fromBundle == null)
                    {
                        Debug.LogError($"[BoardPreview] {style}: '{path}' NOT IN THE BUNDLE.");
                        exit = 1;
                    }
                    else if (!Report(style + "/bundle", fromBundle)) exit = 1;

                    var fromProject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (fromProject == null)
                    {
                        Debug.LogError($"[BoardPreview] {style}: '{path}' not in the PROJECT either.");
                        exit = 1;
                        continue;
                    }
                    if (!Report(style + "/project", fromProject)) exit = 1;
                    // The picture comes from the project copy — see the header.
                    Shoot(style, fromProject, outDir);
                }

                bundle.Unload(true);
                Debug.Log("[BoardPreview] done, exit " + exit);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BoardPreview] FAILED: {e}");
                exit = 1;
            }
            if (Application.isBatchMode) EditorApplication.Exit(exit);
        }

        /// <summary>Dump every number a reviewer would otherwise have to trust, at F5.
        /// Returns false if something structural is missing.</summary>
        private static bool Report(string style, GameObject prefab)
        {
            bool ok = true;
            var inst = (GameObject)Object.Instantiate(prefab);
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;

            Debug.Log($"[BoardPreview] ===== {style} : {prefab.name} =====");

            // Renderer bounds in the ROOT's own frame — the size and the offset of the
            // drawn board against the prefab origin the mod parks everything from.
            var rs = inst.GetComponentsInChildren<MeshRenderer>(true);
            if (rs.Length == 0) { Debug.LogError($"[BoardPreview] {style}: NO MeshRenderer."); return false; }
            Bounds wb = rs[0].bounds;
            foreach (var r in rs) wb.Encapsulate(r.bounds);
            Debug.Log($"[BoardPreview] {style} bounds size = {F(wb.size)}   centre = {F(wb.center)}");

            // Material + the textures that actually travelled in the bundle.
            var mat = rs[0].sharedMaterial;
            if (mat == null) { Debug.LogError($"[BoardPreview] {style}: renderer has NO material."); ok = false; }
            else
            {
                Debug.Log($"[BoardPreview] {style} material '{mat.name}' shader '{(mat.shader ? mat.shader.name : "NULL")}'"
                          + $" (isSupported={(mat.shader ? mat.shader.isSupported.ToString() : "n/a")})");
                if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                {
                    Debug.LogError($"[BoardPreview] {style}: SHADER DID NOT TRAVEL — this renders magenta in game.");
                    ok = false;
                }
                // A bundle-loaded shader compiled for another platform has NO property list
                // here, so HasProperty is false for everything and this block reports nothing.
                // That is the documented viewer artifact, not a missing texture — the project
                // pass right after prints the real bindings. Say which case it is out loud
                // rather than letting a silent "NONE" read as a lost texture.
                int props = mat.shader != null ? ShaderUtil.GetPropertyCount(mat.shader) : 0;
                if (props == 0)
                    Debug.LogWarning($"[BoardPreview]   {style}: shader exposes 0 properties in THIS editor — "
                                     + "no variant for the viewer's graphics API (expected for a Win64 bundle on Linux). "
                                     + "Texture bindings unreadable from this copy; see the project pass.");
                foreach (string p in new[] { "_MainTex", "_BumpMap", "_MRSMap" })
                {
                    if (!mat.HasProperty(p)) continue;
                    var t = mat.GetTexture(p);
                    Debug.Log($"[BoardPreview]   {p} = {(t ? $"{t.name} {t.width}x{t.height}" : "NONE — nothing bound")}");
                }
                foreach (string p in new[] { "_SpecStrength", "_Cull", "_Ambient", "_LightBoost", "_NormalStrength" })
                    if (mat.HasProperty(p))
                        Debug.Log($"[BoardPreview]   {p} = {mat.GetFloat(p).ToString("F3", CultureInfo.InvariantCulture)}");
            }

            // Anchors, at full precision, in the prefab root's frame.
            foreach (string n in AnchorNames)
            {
                var t = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n);
                if (t == null)
                {
                    // Seat 3's legacy alias is optional; the canonical seven are not.
                    bool required = n != "SkipButton" && n != "ConfirmButton" && n != "UndoButton";
                    Debug.Log($"[BoardPreview]   anchor {n,-15} MISSING{(required ? "  <-- REQUIRED" : "")}");
                    if (required) ok = false;
                    continue;
                }
                Debug.Log($"[BoardPreview]   anchor {n,-15} = {F(inst.transform.InverseTransformPoint(t.position))}");
            }

            // The measurement carriers. localPosition.xy is a HALF-extent in metres,
            // not a position — see BoardBuilder. Printed as full mm so it can be read
            // against the expected table without arithmetic.
            foreach (string n in ExtentNames)
            {
                var t = inst.transform.Find(n);
                if (t == null)
                {
                    Debug.LogError($"[BoardPreview]   extent {n,-16} MISSING — the mod falls back to the tuned cap size.");
                    ok = false;
                    continue;
                }
                Vector3 p = t.localPosition;
                Debug.Log($"[BoardPreview]   extent {n,-16} = {(p.x * 2000f).ToString("F1", CultureInfo.InvariantCulture)}"
                          + $" x {(p.y * 2000f).ToString("F1", CultureInfo.InvariantCulture)} mm");
            }

            var cols = inst.GetComponentsInChildren<MeshCollider>(true);
            Debug.Log($"[BoardPreview]   MeshCollider(s): {cols.Length}"
                      + (cols.Length > 0 ? $" convex={cols[0].convex}" : ""));
            if (cols.Length == 0) { Debug.LogError($"[BoardPreview] {style}: NO collider — the finger laser passes through."); ok = false; }

            Object.DestroyImmediate(inst);
            return ok;
        }

        private static string F(Vector3 v) =>
            "(" + v.x.ToString("F5", CultureInfo.InvariantCulture)
                + ", " + v.y.ToString("F5", CultureInfo.InvariantCulture)
                + ", " + v.z.ToString("F5", CultureInfo.InvariantCulture) + ")";

        /// <summary>Two shots per board: flat-on from the viewer side, and a raking
        /// three-quarter that shows the recesses as depth rather than as a drawing.</summary>
        private static void Shoot(string style, GameObject prefab, string outDir)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.LogWarning("[BoardPreview] No graphics device — cannot render. Run under xvfb-run WITHOUT -nographics.");
                return;
            }

            var inst = (GameObject)Object.Instantiate(prefab);
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;

            // BoardLit bakes its own two studio directions and never goes dark, so the
            // scene light below only matters for anything that is NOT BoardLit. Kept so
            // a foreign material would still be visible rather than silently black.
            var lightGo = new GameObject("L");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.0f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.40f);

            var camGo = new GameObject("C");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.09f);
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.01f;

            // The decorated face points -Z (bundle contract), so the viewer sits at -Z.
            Shot(cam, inst, outDir, $"{style}_flat.png",
                 new Vector3(0f, 0f, -0.98f), new Vector3(0f, 0f, 0f), 1800, 1000);
            Shot(cam, inst, outDir, $"{style}_rake.png",
                 new Vector3(0.20f, 0.26f, -0.55f), new Vector3(0.06f, -0.01f, 0f), 1600, 1000);
            // A close crop on the button column (board +X side) — the three seats are the
            // whole point of this rebuild and they are 30 px wide in the flat shot.
            Shot(cam, inst, outDir, $"{style}_seats.png",
                 new Vector3(0.225f, 0.0f, -0.30f), new Vector3(0.225f, 0f, 0f), 1000, 1200);

            // …and the same board again with the user's live asset-pose dials applied the way
            // SetAssetPose applies them. Markers stand in for the pinned control set: they are
            // parented to the ROOT, not to the mesh, exactly as the anchors are re-pinned, so the
            // gap between a marker and the seat it belongs to IS the defect, drawn.
            foreach (var (s, tag, off, eul) in UserAssetPose)
            {
                if (s != style) continue;
                var posed = (GameObject)Object.Instantiate(inst);
                posed.transform.position = Vector3.zero;
                posed.transform.rotation = Quaternion.identity;
                if (ApplyAssetPose(posed, off, eul, $"{style}/{tag}"))
                    Shot(cam, posed, outDir, $"{style}_{tag}.png",
                         new Vector3(0.20f, 0.26f, -0.55f), new Vector3(0.06f, -0.01f, 0f), 1600, 1000);
                Object.DestroyImmediate(posed);
            }

            Object.DestroyImmediate(inst);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
        }

        /// <summary>
        /// Replay <c>Cards/PlayTray.3.Pose.SetAssetPose</c> on a prefab instance: move the mesh
        /// child by <paramref name="offset"/> and <paramref name="euler"/> in the root's frame,
        /// and write every anchor back to the root-local pose it held BEFORE the move — which is
        /// what the mod does, and what makes the control set stay behind when the mesh leaves.
        /// Then park a small marker cube at each seat and rest anchor so the separation is
        /// visible instead of having to be imagined. Returns false if the prefab is not shaped
        /// the way the mod expects, rather than rendering a picture of nothing.
        /// </summary>
        private static bool ApplyAssetPose(GameObject root, Vector3 offset, Vector3 euler, string style)
        {
            Transform visual = null;
            foreach (Transform c in root.transform)
                if (c.GetComponentInChildren<MeshRenderer>(true) != null) { visual = c; break; }
            if (visual == null)
            {
                Debug.LogError($"[BoardPreview] {style}: no mesh child under the root — asset-pose replay skipped.");
                return false;
            }

            var pinned = new System.Collections.Generic.List<(Transform T, Vector3 P, Quaternion R)>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (System.Array.IndexOf(AnchorNames, t.name) >= 0)
                    pinned.Add((t, root.transform.InverseTransformPoint(t.position),
                               Quaternion.Inverse(root.transform.rotation) * t.rotation));

            visual.localPosition += offset;
            visual.localRotation = Quaternion.Euler(euler) * visual.localRotation;
            foreach (var (t, p, r) in pinned)
            {
                t.position = root.transform.TransformPoint(p);
                t.rotation = root.transform.rotation * r;
            }
            // BEFORE the rays, not after: a MeshCollider still reports its pre-move pose until the
            // physics scene is told, so a sync at the end of this method would have measured the
            // mesh where it USED to be and reported 0 mm on a board that had walked 110 mm.
            Physics.SyncTransforms();

            // How far did the mesh walk out from under the pinned set? Measured at the seat
            // anchors, because that is where the keycaps sit: the distance from each anchor to
            // the mesh surface directly behind it along the board normal. On a board that needs no
            // correction this is 0 and the shot is a control.
            float worst = 0f;
            foreach (var (t, _, _) in pinned)
            {
                if (t.name != "ButtonSeat1" && t.name != "ButtonSeat2" && t.name != "ButtonSeat3"
                    && t.name != "ShortRestToken" && t.name != "LongRestToken") continue;
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.transform.SetParent(root.transform, false);
                marker.transform.position = t.position;
                marker.transform.localScale = new Vector3(0.030f, 0.030f, 0.008f);
                var mr = marker.GetComponent<MeshRenderer>();
                mr.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.red };
                Object.DestroyImmediate(marker.GetComponent<BoxCollider>());
                // The mesh's own surface under this anchor, along -Z (the viewer side).
                var ray = new Ray(t.position + Vector3.back * 0.30f, Vector3.forward);
                float gap = float.NaN;
                foreach (var mc in visual.GetComponentsInChildren<MeshCollider>(true))
                    if (mc.Raycast(ray, out RaycastHit hit, 0.60f))
                        gap = Mathf.Abs(Vector3.Dot(hit.point - t.position, Vector3.forward));
                if (!float.IsNaN(gap)) worst = Mathf.Max(worst, gap);
                Debug.Log($"[BoardPreview]   USER ASSET POSE {style}: anchor {t.name,-15} stands "
                          + (float.IsNaN(gap)
                             ? "OFF THE MESH ENTIRELY (no surface behind it)"
                             : $"{gap * 1000f:F1} mm off the mesh surface behind it"));
            }
            // THE ALARM THRESHOLD IS THE MOD'S OWN BUDGET, not a round number. BoardAnchors caps
            // the lift at MaxAnchorLift = 5 mm, and BuildBoard seats every anchor 0.5 mm proud of
            // the floor to begin with, so 5.5 mm is the largest separation a CLAMPED pose can
            // legitimately produce. An alarm that fired below it would cry wolf at a passing case,
            // which is how an instrument stops being read.
            const float alarmAt = 0.0055f;
            Debug.Log($"[BoardPreview] USER ASSET POSE {style}: offset {F(offset)} euler {F(euler)} "
                      + $"-> worst anchor-to-mesh separation {worst * 1000f:F1} mm "
                      + (worst <= alarmAt
                         ? "(within the 5.0 mm lift budget + the 0.5 mm proud seat — the control set sits on the board)"
                         : "*** THE CONTROL SET DOES NOT SIT ON THE BOARD ***"));
            return true;
        }

        private static void Shot(Camera cam, GameObject inst, string outDir, string file,
                                 Vector3 eye, Vector3 look, int w, int h)
        {
            cam.transform.position = eye;
            cam.transform.LookAt(look);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 8;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            string path = Path.Combine(outDir, file);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            Debug.Log($"[BoardPreview] wrote {path}");
        }
    }
}
