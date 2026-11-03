using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthCardProgram
{
    [HarmonyPatch]
    private static class PerkClipPorts756
    {
        [HarmonyTargetMethods] private static IEnumerable<MethodBase> Targets()
        {
            foreach (Type type in new[] { typeof(UIPerksWindow), typeof(UIPerksActiveModifiers), typeof(UIWindow), typeof(UITooltip) })
                foreach (string name in new[] { "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy" })
                { MethodInfo method = AccessTools.DeclaredMethod(type, name); if (method != null) yield return method; }
        }
        [HarmonyPrefix] private static bool Before() => false;
    }

    private sealed partial class Scene
    {
        internal IEnumerator ProvePerkClip756()
        {
            _root.sizeDelta = new Vector2(2400, 1800);
            _head.transform.SetPositionAndRotation(new Vector3(0, 0, -3), Quaternion.identity);
            var nativeWindow = _root.gameObject.AddComponent<UIWindow>();
            var window = _root.gameObject.AddComponent<UIPerksWindow>();
            Field(window, "window", nativeWindow);
            Field(nativeWindow, "m_CurrentVisualState", Enum.Parse(typeof(UIWindow.VisualState), "Shown"));
            var summaryRect = (RectTransform)Go("original right modifier summary", _root).transform;
            summaryRect.gameObject.layer = 5; summaryRect.sizeDelta = new Vector2(2000, 1600);
            var summary = summaryRect.gameObject.AddComponent<UIPerksActiveModifiers>();
            Field(window, "activePerkssDisplay", summary);
            var frame = (RectTransform)Go("original conditional modifier frame", summaryRect).transform;
            frame.gameObject.layer = 5; frame.sizeDelta = new Vector2(712, 100); frame.anchoredPosition = new Vector2(300, 400);
            Field(summary, "conditionalModifContainer", frame);
            var card = new Card(typeof(UIPerkAttackModifier), frame, 0);
            card.Rect.sizeDelta = new Vector2(100, 100); card.Rect.anchorMin = card.Rect.anchorMax = card.Rect.pivot = new Vector2(.5f, .5f);
            card.Rect.anchoredPosition = new Vector2(-805, -644); // Exact original cold prefab pose.
            foreach (Graphic graphic in card.Rect.GetComponentsInChildren<Graphic>(true)) graphic.enabled = false;
            Image backing = card.Backgrounds[0]; backing.enabled = true; backing.color = Color.blue;
            backing.canvasRenderer.SetAlpha(.73f);
            var pool = new List<UIPerkAttackModifier> { (UIPerkAttackModifier)card.Owner };
            Field(summary, "conditionalPool", pool);
            card.Rect.gameObject.SetActive(true); MenuDepthDriver.ObserveOwner(card.Owner); MenuDepthDriver.Tick();
            Vector3 cold = card.Rect.localPosition;
            ++_request; while (_completed != _request) yield return null;
            Check(Blue756("cold-outside") == 0,
                "a wholly out-of-frame native summary cell paints no stray card in final head pixels");
            Check(card.Rect.localPosition == cold && card.Rect.gameObject.activeSelf && Mathf.Abs(backing.canvasRenderer.GetAlpha() - .73f) < .0001f,
                "out-of-row clipping restores exact original pose, activity and native renderer alpha after capture");
            var plane = card.Rect.GetComponent<MenuDepthCardPlane>();
            Vector3 target = card.Rect.position;
            Check(!plane.TryRaycast(target - Vector3.forward, Vector3.forward, 3f, out _),
                "a clipped native summary orphan has no phantom raised-card ray offer");
            card.Rect.anchoredPosition = Vector2.zero;
            ++_request; while (_completed != _request) yield return null;
            Check(Blue756("valid-summary") > 25,
                "later native valid layout immediately restores the original right summary card in final head pixels");
            card.Rect.anchoredPosition = new Vector2(frame.rect.xMax + 30, 0);
            ++_request; while (_completed != _request) yield return null;
            Check(Blue756("intersecting-summary") > 25,
                "native summary cells intersecting their owner frame are retained, including animated partial cells");
            card.Rect.anchoredPosition = new Vector2(-805, -644); pool.Clear();
            ++_request; while (_completed != _request) yield return null;
            Check(Blue756("unproven-owner") > 25,
                "an unproven native modifier owner remains untouched instead of guessing from names or cold coordinates");
            pool.Add((UIPerkAttackModifier)card.Owner);
            var additionalFrame = (RectTransform)Go("original additional modifier frame", summaryRect).transform;
            additionalFrame.gameObject.layer = 5; additionalFrame.sizeDelta = new Vector2(350, 100); additionalFrame.anchoredPosition = new Vector2(250, -200);
            Field(summary, "additionalModifiersContainer", additionalFrame);
            var additionalPool = new List<UIPerkAttackModifier> { (UIPerkAttackModifier)card.Owner };
            Field(summary, "additionalModifiersPool", additionalPool); pool.Clear();
            card.Rect.SetParent(additionalFrame, false); card.Rect.anchoredPosition = new Vector2(-805, -644);
            MenuDepthDriver.ObserveOwner(card.Owner); MenuDepthDriver.Tick();
            ++_request; while (_completed != _request) yield return null;
            Check(Blue756("outside-additional") == 0,
                "exact native additional pool cells outside their own frame also leave no stray backing");
            card.Rect.anchoredPosition = Vector2.zero;
            ++_request; while (_completed != _request) yield return null;
            Check(Blue756("valid-additional") > 25,
                "useful additional attack modifier summary remains visible inside its own frame");
            var hover = (RectTransform)Go("actual shared native hover presentation", _root).transform;
            var tooltip = hover.gameObject.AddComponent<UITooltip>();
            AccessTools.Field(typeof(UITooltip), "mInstance").SetValue(null, tooltip);
            hover.gameObject.layer = 5; hover.anchoredPosition = new Vector2(0, -200);
            card.Rect.SetParent(hover, false); card.Rect.anchoredPosition = Vector2.zero;
            MenuDepthDriver.ObserveOwner(card.Owner); MenuDepthDriver.Tick();
            ++_request; while (_completed != _request) yield return null;
            Check(Blue756("live-hover") > 25,
                "a real native hover reparent restores presentation without inheriting summary clipping");
            pool.Add((UIPerkAttackModifier)card.Owner);
            card.Rect.SetParent(frame, false); card.Rect.anchoredPosition = new Vector2(-805, -644);
            MenuDepthDriver.ObserveOwner(card.Owner); MenuDepthDriver.Tick();
            Field(nativeWindow, "m_CurrentVisualState", Enum.Parse(typeof(UIWindow.VisualState), "Hidden"));
            ++_request; while (_completed != _request) yield return null;
            Check(Blue756("closed-perks") > 25 && backing.canvasRenderer.GetAlpha() > .72f,
                "closing the native perks page releases its clipping and retains original renderer state");
            card.Rect.gameObject.SetActive(false);
            AccessTools.Field(typeof(UITooltip), "mInstance").SetValue(null, null);
        }

        private int Blue756(string name)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = _final;
            var image = new Texture2D(_final.width, _final.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply(); RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(_output, "perk-clip756-" + name + ".png"), image.EncodeToPNG());
            int count = 0; foreach (Color32 pixel in image.GetPixels32()) if (pixel.b > 100 && pixel.r < 30 && pixel.g < 30) count++;
            Object.DestroyImmediate(image); return count;
        }
    }
}
