using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.WorldUI.MenuDepth;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthCardProgram
{
    private static readonly Dictionary<Graphic, Vector3[]?> HeadMeshes736 = new();
    [HarmonyPatch(typeof(Graphic), "DoMeshGeneration")]
    private static class HeadMeshObserver736
    {
        [HarmonyPostfix]
        private static void After(Graphic __instance)
        {
            if (!HeadMeshes736.ContainsKey(__instance)) return;
            // Observe the exact native worker mesh immediately after its real
            // IMeshModifier chain and CanvasRenderer.SetMesh. Unity2021 has no
            // CanvasRenderer.GetMesh API. No mesh generation is substituted.
            var mesh = (Mesh)typeof(Graphic).GetProperty("workerMesh", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            HeadMeshes736[__instance] = mesh.vertices;
        }
    }

    private sealed partial class Scene
    {
        internal IEnumerator ProveHeadText736()
        {
            File.WriteAllText(Path.Combine(_output, "scope.txt"),
                "Actual MenuDepthDisplay.RenderHead coordinator, real original TMP glyphs and native card identities. " +
                "Native GUI/AbilityCard_Shd source/depth programs use declared GL/Vulkan stand-ins only; " +
                "this does not execute native D3D11 effects. API=" + SystemInfo.graphicsDeviceType +
                "; reversedZ=" + SystemInfo.usesReversedZBuffer + "\n");
            Card card = _cards[0];
            ((RectTransform)card.Button.transform).sizeDelta = new Vector2(160, 48);
            var buttonLabel = Text((RectTransform)card.Button.transform, "original native card button label",
                "CONTINUE", new Vector2(160, 48), Vector2.zero);
            buttonLabel.fontSize = 24; buttonLabel.color = Color.yellow;
            card.Rect.localScale = new Vector3(.65f, .65f, 1f);
            card.Rect.gameObject.SetActive(true); MenuDepthDriver.Tick();
            var plane = card.Rect.GetComponent<MenuDepthCardPlane>();
            Check(plane != null && plane.IsReady, "the actual original card is enrolled for the head text regression");
            Vector3 nativePosition = card.Rect.localPosition, nativeScale = card.Rect.localScale;
            var helpers = card.Rect.GetComponentsInChildren<MenuDepthGraphic>(true);
            Check(helpers.Length == card.Backgrounds.Count, "every exact original card backing owns its real head-depth helper");
            var native = new Material(Shader.Find("GUI/AbilityCard_Shd"));
            foreach (var background in card.Backgrounds) HeadMeshes736[background] = null;
            try
            {
                for (int style = 0; style < 2; style++)
                {
                    foreach (var background in card.Backgrounds) background.material = style == 0 ? null : native;
                    for (int pose = 0; pose < 32; pose++)
                    {
                        float phase = pose * .37f;
                        Vector3 eye = new Vector3(.18f + .00007f * pose,
                            Mathf.Sin(phase) * .28f, (pose < 16 ? -1f : 1f) * (.60f + .00009f * pose));
                        _head.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(-eye, Vector3.up));
                        _rightEye = (pose & 1) != 0;
                        foreach (var helper in helpers) helper.enabled = true;
                        ++_request; while (_completed != _request) yield return null;
                        Texture2D actualHead = Read(card, true, 73600 + style * 100 + pose);
                        Texture2D actualSource = ReadHeadTextSource736(plane!, style, pose, "actual");
                        Canvas.ForceUpdateCanvases();
                        foreach (var background in card.Backgrounds)
                        {
                            Vector3[]? vertices = HeadMeshes736[background];
                            bool restored = vertices != null && vertices.Length != 0;
                            if (vertices != null) foreach (Vector3 vertex in vertices) restored &= Mathf.Abs(vertex.z) < 1e-8f;
                            Check(restored, "ending head capture restores the original native backing mesh plane");
                        }
                        // Native colour reference: same complete card root loan,
                        // original geometry/shader/TMP/order, without depth from
                        // the mod's backdrop helpers. No gameplay or text writes.
                        foreach (var helper in helpers) helper.enabled = false;
                        ++_request; while (_completed != _request) yield return null;
                        Texture2D expectedHead = Read(card, false, 73600 + style * 100 + pose);
                        Texture2D expectedSource = ReadHeadTextSource736(plane!, style, pose, "native");
                        try
                        {
                            CompareHeadText736(actualSource, expectedSource, style, pose, "source");
                            CompareHeadText736(actualHead, expectedHead, style, pose, "final-head");
                            Check(card.Rect.localPosition == nativePosition && card.Rect.localScale == nativeScale,
                                "head text sampling retains the original complete card pose and scale");
                        }
                        finally
                        {
                            Object.DestroyImmediate(actualHead); Object.DestroyImmediate(actualSource);
                            Object.DestroyImmediate(expectedHead); Object.DestroyImmediate(expectedSource);
                        }
                    }
                }
            }
            finally { HeadMeshes736.Clear(); Object.DestroyImmediate(native); foreach (var helper in helpers) if (helper != null) helper.enabled = true; }
        }

        private Texture2D ReadHeadTextSource736(MenuDepthCardPlane plane, int style, int pose, string name)
        {
            var target = (RenderTexture)typeof(MenuDepthDisplay).GetField(_rightEye ? "_headRight" : "_headLeft",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_display)!;
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(_output, name + "-source-" + style + "-" + pose + ".png"), pixels.EncodeToPNG());
            Check(plane.IsReady, "moving head text captures retain the actual live native card owner");
            return pixels;
        }

        private static void CompareHeadText736(Texture2D actual, Texture2D expected, int style, int pose, string part)
        {
            Color32[] a = actual.GetPixels32(), b = expected.GetPixels32();
            int wanted = 0, buttonPixels = 0, lost = 0; long totalError = 0;
            for (int i = 0; i < b.Length; i++)
            {
                if (b[i].r < 180 || b[i].g < 180) continue;
                if (b[i].b >= 180) wanted++;
                else if (b[i].b < 80) buttonPixels++;
                else continue;
                int error = Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
                totalError += error;
                if (error > 12) lost++;
            }
            File.AppendAllText(Path.Combine(_output, "head-text-coverage.csv"), style + "," + pose + "," + part +
                ",wanted=" + wanted + ",button=" + buttonPixels + ",lost=" + lost + ",error=" + totalError + "\n");
            Check(wanted > 60, "the native original title has visible real TMP coverage at every head angle");
            Check(buttonPixels > 12, "the native original button label has visible real TMP coverage at every head angle");
            Check(lost == 0, "coplanar original card text stays intact over the live head-depth background");
        }
    }
}
