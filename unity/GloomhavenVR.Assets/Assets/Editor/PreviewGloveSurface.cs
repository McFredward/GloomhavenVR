// Compare the shipped glove geometry/albedo with temporary normal-strength overrides.
// GLOVE_SURFACE_PREVIEW_OUT=/tmp/glove-preview Unity -batchmode -projectPath <project>
//   -executeMethod GloomhavenVR.GloveSurfacePreview.RenderAll -logFile <log> -quit
// Use the pinned Unity 2021.3.5 editor with a graphics device (no -nographics).
// No authored material, mesh, texture or scene is changed. BoardLit supplies its own
// studio lighting. These renders isolate surface shading; they do not establish headset parity.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GloomhavenVR
{
    public static class GloveSurfacePreview
    {
        private const string Hands = "Assets/Bundle/Hands/";
        private const int Resolution = 900;
        private static readonly float[] Strengths = { 0.5f, 0.25f, 0f };
        private static readonly string[] Digits = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
        private static readonly string[] Joints = { "Root", "Mid", "Tip" };

        public static void RenderAll()
        {
            string output = Environment.GetEnvironmentVariable("GLOVE_SURFACE_PREVIEW_OUT");
            if (string.IsNullOrEmpty(output))
                throw new InvalidOperationException("Set GLOVE_SURFACE_PREVIEW_OUT to an external output directory.");
            Directory.CreateDirectory(output);
            foreach (string side in new[] { "L", "R" })
                foreach (float curl in new[] { 0f, 0.4f })
                    foreach (bool palm in new[] { false, true })
                        RenderComparison(output, side, curl, palm);
            Debug.Log("[GloveSurfacePreview] Complete: 24 individual renders and eight comparisons. "
                + "Comparison columns: 0.50 (previous), 0.25 (candidate), 0.00 (diagnostic). "
                + "Authored assets unchanged; editor lighting is not headset evidence.");
        }

        private static void RenderComparison(string output, string side, float curl, bool palmView)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var materials = new List<Material>();
            GameObject hand = null;
            GameObject cameraObject = null;
            RenderTexture target = null;
            Texture2D comparison = null;
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Hands + "VRHand_" + side + ".prefab");
                if (prefab == null) throw new InvalidOperationException("Missing glove prefab " + side);
                hand = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(hand, scene);
                hand.transform.position = Vector3.zero;
                hand.transform.rotation = Quaternion.identity;
                hand.transform.localScale *= 1.12f; // Current default Glove style scale.
                foreach (Renderer renderer in hand.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] clones = renderer.sharedMaterials;
                    for (int i = 0; i < clones.Length; i++)
                    {
                        if (clones[i] == null || !clones[i].HasProperty("_NormalStrength")
                            || clones[i].shader.name != "GloomhavenVR/BoardLit")
                            throw new InvalidOperationException("Glove must use its native BoardLit material.");
                        clones[i] = new Material(clones[i]);
                        materials.Add(clones[i]);
                    }
                    renderer.sharedMaterials = clones;
                    if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
                }
                if (materials.Count == 0) throw new InvalidOperationException("Glove has no materials.");

                // Match FingerCurler's default local-X bend. This is reproduced arithmetic,
                // not its runtime mode/input machine; a player's configured curl can differ.
                foreach (string digit in Digits)
                {
                    Vector3 degrees = digit == "Thumb" ? new Vector3(25f, 45f, 60f) : new Vector3(75f, 95f, 65f);
                    for (int i = 0; i < Joints.Length; i++)
                    {
                        Transform bone = Require(hand, "Anchor_" + digit + "_" + Joints[i]);
                        bone.localRotation *= Quaternion.Euler(degrees[i] * curl, 0f, 0f);
                    }
                }

                // Finger-focused framing uses actual posed joints rather than the renderer's
                // imported rest bounds. Keep the identical camera for all three strengths.
                Transform palm = Require(hand, "Anchor_Palm");
                Transform wrist = Require(hand, "Anchor_Wrist");
                Vector3 up = (Require(hand, "Anchor_Middle_Root").position - wrist.position).normalized;
                Vector3 outward = palm.up * (palmView ? 1f : -1f);
                Vector3 view = (outward + up * 0.2f).normalized;
                Bounds focus = new Bounds(Require(hand, "Anchor_Index_Root").position, Vector3.zero);
                foreach (string digit in Digits)
                    foreach (string joint in Joints)
                        focus.Encapsulate(Require(hand, "Anchor_" + digit + "_" + joint).position);
                focus.Expand(0.038f); // Includes flesh beyond terminal joints and silhouette margin.

                cameraObject = new GameObject("Glove surface preview camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.10f, 0.11f, 0.13f, 1f);
                camera.orthographic = true;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 5f;
                camera.transform.position = focus.center + view;
                camera.transform.LookAt(focus.center, up);
                float span = 0f;
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = focus.center + Vector3.Scale(focus.extents, new Vector3(x, y, z));
                            Vector3 local = camera.transform.InverseTransformPoint(corner);
                            span = Mathf.Max(span, Mathf.Abs(local.x), Mathf.Abs(local.y));
                        }
                camera.orthographicSize = span;
                target = new RenderTexture(Resolution, Resolution, 24) { antiAliasing = 4 };
                target.Create();
                camera.targetTexture = target;
                comparison = new Texture2D(Resolution * Strengths.Length, Resolution, TextureFormat.RGB24, false);
                string stem = "glove_" + side + (curl == 0f ? "_open" : "_curl") + (palmView ? "_palm" : "_back");
                for (int column = 0; column < Strengths.Length; column++)
                {
                    foreach (Material material in materials) material.SetFloat("_NormalStrength", Strengths[column]);
                    camera.Render();
                    RenderTexture.active = target;
                    var image = new Texture2D(Resolution, Resolution, TextureFormat.RGB24, false);
                    try
                    {
                        image.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
                        image.Apply();
                        string suffix = Strengths[column].ToString("F2", CultureInfo.InvariantCulture);
                        File.WriteAllBytes(Path.Combine(output, stem + "_" + suffix + ".png"), image.EncodeToPNG());
                        comparison.SetPixels(column * Resolution, 0, Resolution, Resolution, image.GetPixels());
                    }
                    finally { Object.DestroyImmediate(image); }
                }
                comparison.Apply();
                File.WriteAllBytes(Path.Combine(output, stem + "_comparison.png"), comparison.EncodeToPNG());
                Debug.Log("[GloveSurfacePreview] " + stem + ": left-to-right normal strength 0.50 / 0.25 / 0.00.");
                camera.targetTexture = null;
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (comparison != null) Object.DestroyImmediate(comparison);
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                if (hand != null) Object.DestroyImmediate(hand);
                foreach (Material material in materials) Object.DestroyImmediate(material);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // Separate batch method; pass the rebuilt bundle path in GLOVE_SURFACE_BUNDLE.
        // Checks actual bundled materials and every runtime attachment/curl anchor on all styles.
        public static void VerifyBundle()
        {
            string path = Environment.GetEnvironmentVariable("GLOVE_SURFACE_BUNDLE");
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Set GLOVE_SURFACE_BUNDLE.");
            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) throw new InvalidOperationException("Cannot load bundle " + path);
            try
            {
                foreach (string style in new[] { "VRHand", "VRHandPlate", "VRHandArcane" })
                    foreach (string side in new[] { "L", "R" })
                    {
                        var hand = bundle.LoadAsset<GameObject>((Hands + style + "_" + side + ".prefab").ToLowerInvariant());
                        if (hand == null) throw new InvalidOperationException("Missing bundled hand " + style + side);
                        foreach (string name in new[] { "Anchor_Wrist", "Anchor_Palm", "Anchor_IndexTip", "Anchor_Grab" })
                            Require(hand, name);
                        foreach (string digit in Digits)
                            foreach (string joint in Joints) Require(hand, "Anchor_" + digit + "_" + joint);
                        int count = 0;
                        foreach (Renderer renderer in hand.GetComponentsInChildren<Renderer>(true))
                            foreach (Material material in renderer.sharedMaterials)
                            {
                                float expected = style == "VRHand" ? 0.25f : 1f;
                                if (material == null || material.shader.name != "GloomhavenVR/BoardLit"
                                    || !material.HasProperty("_NormalStrength")
                                    || !Mathf.Approximately(material.GetFloat("_NormalStrength"), expected)
                                    || material.GetTexture("_MainTex") == null || material.GetTexture("_BumpMap") == null)
                                    throw new InvalidOperationException("Unexpected bundled material on " + style + side);
                                count++;
                            }
                        if (count == 0) throw new InvalidOperationException("Missing bundled renderers on " + style + side);
                        Debug.Log("[GloveSurfacePreview] Bundle verified " + style + "_" + side
                            + ": 19 anchors, " + count + " textured BoardLit material slots, expected normal strength.");
                    }
            }
            finally { bundle.Unload(true); }
        }

        private static Transform Require(GameObject root, string name)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                if (transform.name == name) return transform;
            throw new InvalidOperationException(root.name + " is missing " + name);
        }
    }
}
