// GloomhavenVR companion project — DOES THE CONTROLLER SIT IN THE HAND THE RIGHT WAY ROUND?
//
// Batch: Unity.exe -batchmode -projectPath <this project> -buildTarget Win64
//        -executeMethod GloomhavenVR.ControllersPreview.RenderAll -logFile preview-controllers.log
//        (needs a display: run under xvfb-run.)
//
// WHY THIS EXISTS. The runtime parents the controller model to VRHand.transform — the raw
// devicePosition/deviceRotation — on the claim that the webxr-input-profiles models are authored
// in the same GRIP SPACE that OpenXR reports. That claim was reasoned, not measured, and it is
// the one assumption in the whole feature that fails INVISIBLY on this machine: a wrong frame
// still shows a controller, in the hand, at the right size, merely rotated. The hand's own seat
// is no help either — the glove sits at pitch -51, roll -109, yaw -35 from the device pose, so
// "it looks about right" is not an argument anybody can make from the numbers.
//
// So this renders both together in the ONE frame the runtime actually builds:
//     device pose ── (identity) ────────────────> controller model
//                 └─ (shipped [Hands] seat) ────> hand prefab
// If the frames agree, the controller lies along the palm with its trigger under the index
// finger. If they do not, it is somewhere obviously wrong, which is the entire point.

using System.IO;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR
{
    public static class ControllersPreview
    {
        private const string Out = "Build/PreviewControllers";
        private const int Size = 620;

        // The SHIPPED glove seat (Defaults.Hands.cs). Mirrored terms use the right hand's
        // mirror = +1; see VRHand.SyncVisualOffset for why roll/yaw/spread are mirrored and
        // pitch is not.
        private const float SeatPitch = -51f;
        private const float SeatRoll = -109f;
        private const float SeatYaw = -35f;
        private const float SeatLateral = -0.01f;
        private const float SeatVertical = 0.061f;
        private const float SeatForward = -0.054f;
        private const float SeatSpread = 0.07f;

        // The candidate offset under test. CTRL_OFFSET / CTRL_EULER override them so a
        // sweep can be rendered without recompiling.
        private static Vector3 Offset => Parse("CTRL_OFFSET", Vector3.zero);
        private static Vector3 Euler => Parse("CTRL_EULER", Vector3.zero);

        private static Vector3 Parse(string key, Vector3 fallback)
        {
            string raw = System.Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(raw)) return fallback;
            string[] parts = raw.Split(',');
            if (parts.Length != 3) return fallback;
            return new Vector3(float.Parse(parts[0]), float.Parse(parts[1]),
                               float.Parse(parts[2]));
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                Transform hit = Find(c, name);
                if (hit != null) return hit;
            }
            return null;
        }

        public static void RenderAll()
        {
            Directory.CreateDirectory(Out);
            string only = System.Environment.GetEnvironmentVariable("CTRL_ONLY");
            foreach (string id in string.IsNullOrEmpty(only)
                     ? new[] { "quest3", "pico4", "index", "generic" } : new[] { only })
                Render(id);
            Debug.Log($"[GloomhavenVR] controller previews -> {Path.GetFullPath(Out)}");
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        private static void Render(string id)
        {
            var stage = new GameObject($"stage_{id}");

            // The device pose. Everything the runtime hangs off VRHand.transform hangs off this.
            var device = new GameObject("DevicePose");
            device.transform.SetParent(stage.transform, false);

            string prefabPath =
                $"Assets/Bundle/Controllers/{id}/Controller_{id}_right.prefab";
            var controllerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (controllerPrefab == null)
            {
                Debug.LogError($"[GloomhavenVR] {prefabPath} missing — run ControllersBuilder first.");
                Object.DestroyImmediate(stage);
                return;
            }
            var controller = (GameObject)PrefabUtility.InstantiatePrefab(controllerPrefab);

            // The hand, at its shipped seat below the same device pose.
            var handPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Bundle/Hands/VRHand_R.prefab");
            if (handPrefab != null)
            {
                var hand = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab);
                hand.transform.SetParent(device.transform, false);
                hand.transform.localPosition =
                    new Vector3(SeatLateral + SeatSpread, SeatVertical, SeatForward);
                hand.transform.localRotation = Quaternion.Euler(-SeatPitch, SeatYaw, SeatRoll);

                // THE DEVICE POSE, and the alternative that was measured and rejected.
                //
                // The controller hangs off the raw device pose, at identity, which is where the
                // runtime puts it. This preview exists because that was originally a REASONED
                // claim, and the first render appeared to refute it: the controller did not sit
                // in the glove's hand. The glove was the red herring. Two measurements settled it:
                //
                //  * Anchor_Grab sits 4 cm from the device pose and about 90 degrees off it. The
                //    hand art is stylised and hand-seated (the shipped glove carries a 7 cm
                //    comfort SPREAD), so it is not physically registered and cannot be used as
                //    evidence about where a real device is.
                //  * The generic profile ships a POINTING_POSE node — the OpenXR AIM pose relative
                //    to the GRIP pose the models are authored around. After conversion it lands
                //    7.2 cm forward, 3.0 cm below, pitched 17.5 degrees down from +Z: exactly
                //    OpenXR's grip-to-aim relationship. So the converted model frame IS the frame
                //    Unity's devicePosition/deviceRotation report, and identity is correct.
                //
                // Parenting to Anchor_Grab was tried (CTRL_GRAB=1 still does it) and buries the
                // controller in the wrist, which is what a frame mismatch looks like.
                if (System.Environment.GetEnvironmentVariable("CTRL_GRAB") == "1")
                {
                    Transform grab = Find(hand.transform, "Anchor_Grab") ?? hand.transform;
                    controller.transform.SetParent(grab, false);
                    controller.transform.localPosition = Offset;
                    controller.transform.localRotation = Quaternion.Euler(Euler);
                }
                else
                {
                    controller.transform.SetParent(device.transform, false);
                }
            }
            else
            {
                controller.transform.SetParent(device.transform, false);
                Debug.LogWarning("[GloomhavenVR] VRHand_R.prefab missing — controller shown alone.");
            }

            // A stub in the device's own +Z, so the render says which way the frame points
            // rather than leaving it to be inferred from the model.
            var axis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            axis.name = "DeviceForward";
            axis.transform.SetParent(device.transform, false);
            axis.transform.localPosition = new Vector3(0f, 0f, 0.09f);
            axis.transform.localScale = new Vector3(0.004f, 0.004f, 0.11f);
            axis.GetComponent<Renderer>().sharedMaterial =
                new Material(Shader.Find("Unlit/Color")) { color = new Color(0.2f, 1f, 0.4f) };

            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.09f);
            cam.orthographic = true;
            cam.orthographicSize = 0.16f;

            var light = new GameObject("light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.transform.rotation = Quaternion.Euler(38f, 150f, 0f);

            var angles = new (string Name, Vector3 Euler)[]
            {
                ("palm", new Vector3(12f, 205f, 0f)),
                ("back", new Vector3(12f, 25f, 0f)),
                ("above", new Vector3(78f, 180f, 0f)),
            };
            foreach ((string name, Vector3 euler) in angles)
            {
                cam.transform.rotation = Quaternion.Euler(euler);
                cam.transform.position = new Vector3(0f, 0.01f, 0f)
                                         - cam.transform.forward * 0.6f;
                var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                File.WriteAllBytes($"{Out}/{id}_{name}.png", tex.EncodeToPNG());
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }

            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(light.gameObject);
            Object.DestroyImmediate(stage);
        }
    }
}
