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
    private static Scene? _hoverScene;

    // Execute the real native ToggleFullCardPreview, including activation, layout,
    // its transient Canvas and BlockRaycasts. Gameplay/asset services and the
    // already-established TooltipOnWindow placement are explicit test ports.
    [HarmonyPatch]
    private static class NativeHoverPorts
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(AbilityCardUI)))
                if (method.Name == "SetDisplayMode" || method.Name == "ChangeFullCardPosition") yield return method;
            yield return AccessTools.Method(typeof(FullAbilityCard), "Highlight");
            yield return AccessTools.Method(typeof(FullAbilityCard), "ToggleBurningAnimation");
        }
        [HarmonyPrefix]
        private static bool Before(object __instance, MethodBase __originalMethod)
        {
            if (__originalMethod.Name == "ChangeFullCardPosition") _hoverScene!.PlaceHover((AbilityCardUI)__instance);
            return false;
        }
    }

    private sealed partial class Scene
    {
        internal void PlaceHover(AbilityCardUI row)
        {
            // TooltipOnWindow.PlaceFullCardPreview/Settle source contract: carry
            // native XY placement to the enrolled content root, flatten original
            // Z/rotation, preserve physical size and sort above the native host.
            RectTransform full = row.fullAbilityCard.RectTransform;
            Vector3 worldScale = full.lossyScale;
            full.SetParent(_root, false);
            full.localScale = new Vector3(worldScale.x / _root.lossyScale.x,
                worldScale.y / _root.lossyScale.y, worldScale.z / _root.lossyScale.z);
            full.localRotation = Quaternion.identity; full.localPosition = new Vector3(40, -45, 0);
            full.SetAsLastSibling();
            Canvas canvas = full.GetComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingOrder = 15;
        }

        internal IEnumerator ProveNativeHover729()
        {
            _hoverScene = this;
            var sources=(IList)AccessTools.Field(typeof(MenuDepthSources),"Sources").GetValue(null)!;
            int coldHelpers=0,registeredColdHelpers=0,destroyedHelpers=0;
            foreach(Card cold in _cards)
                foreach(Image background in cold.Backgrounds)
                {
                    MenuDepthGraphic helper=background.GetComponent<MenuDepthGraphic>();
                    if(helper==null)continue;
                    coldHelpers++;
                    if(sources.Contains(helper))registeredColdHelpers++;
                }
            foreach(object source in sources)if(source is MenuDepthGraphic helper&&helper==null)destroyedHelpers++;
            if(_path<2)Check(coldHelpers>0,"native pool discovery prepares original background helpers before their first activation");
            File.AppendAllText(Path.Combine(_output,"cold-pool-registry.csv"),_name+",coldHelpers="+coldHelpers
                +",registeredColdHelpers="+registeredColdHelpers+",destroyedHelpers="+destroyedHelpers+"\n");
            _head.transform.SetPositionAndRotation(new Vector3(.25f, .08f, -.60f),
                Quaternion.LookRotation(new Vector3(-.25f, -.08f, .60f), Vector3.up));
            int censuses = MenuDepthDriver.RootCensuses, visits = MenuDepthDriver.BehaviourVisits;
            var fit = (RectTransform)Go("native character ability subview fit", _root).transform;
            fit.localScale = new Vector3(.65f, .65f, 1f);
            GameObject rowGo = Go("actual native pooled AbilityCardUI overview row", fit); rowGo.SetActive(false);
            var row = rowGo.AddComponent<AbilityCardUI>(); row.enabled = false;
            var full = new Card(typeof(FullAbilityCard), (RectTransform)rowGo.transform, 0);
            var mini = new Card(typeof(MiniAbilityCard), (RectTransform)rowGo.transform, 1);
            full.Owner.enabled = mini.Owner.enabled = false;
            Field(row, "fullAbilityCard", full.Owner); Field(row, "miniAbilityCard", mini.Owner);
            Field(row, "mode", CardHandMode.DeckSelection); Field(row, "currentFullCardSpacing", 33f);
            Field(row, "previewCardHeight", 55f);
            rowGo.SetActive(true);
            full.Rect.gameObject.layer = mini.Rect.gameObject.layer = 5;
            full.Rect.gameObject.SetActive(false); mini.Rect.gameObject.SetActive(false);
            for (int reuse = 0; reuse < 6; reuse++)
            {
                // Exact audited pointer/gamepad Full rect sizes. Physical relief
                // follows either authored shape without renormalizing its scale.
                full.Rect.sizeDelta = reuse < 3 ? new Vector2(294, 450) : new Vector2(360, 555);
                // No Driver.Tick after the real request. The native method adds
                // its Canvas and detaches the Full from its pooled row before
                // the first automatic frame selects capture sources.
                row.ToggleFullCardPreview(true);
                Check(full.Rect.parent == _root && full.Rect.GetComponent<Canvas>().overrideSorting,
                    "native character hover uses the exact FullAbilityCard field with a detached original Canvas");
                mini.Rect.gameObject.SetActive(false);
                Vector3 nativePosition = full.Rect.localPosition, nativeScale = full.Rect.localScale;
                ++_request; while (_completed != _request) yield return null;
                Texture2D first = Read(full, true, 300 + reuse);
                White(first);
                var marker = full.Rect.GetComponent<MenuDepthCardPlane>();
                Check(marker != null && marker.IsReady,
                    "native character hover admits its detached disabled-controller card before the first automatic draw");
                marker!.enabled = false;
                ++_request; while (_completed != _request) yield return null;
                Texture2D flat = Read(full, false, 300 + reuse);
                Vector3 expected = Expected(full, White(flat));
                float error = Error(White(first), expected, first);
                File.AppendAllText(Path.Combine(_output, "native-hover-geometry.csv"),
                    _name + "," + reuse + ",expected=" + expected.ToString("F6") + ",error=" + error + "\n");
                Check(error < 1.8f,
                    "native character hover renders complete original card text at physical depth on its first automatic draw");
                float displacement = Vector2.Scale(White(first).Center - White(flat).Center, new Vector2(first.width, first.height)).magnitude;
                Check(displacement > .7f,
                    "native character hover has meaningful visible protrusion from the flat window");
                // Compare two actual rasters. At the smaller native hover fit,
                // a subpixel glyph centroid cannot support a percentage against
                // an ideal continuous projection. The main geometry proof still
                // requires the independent 10.4% plane and rejects the old plane.
                FieldInfo ratio = typeof(MenuDepthCardPlane).GetField("_depthWidthRatio", BindingFlags.Instance | BindingFlags.NonPublic)!;
                object configured = ratio.GetValue(marker)!;
                Texture2D priorPixels;
                try
                {
                    ratio.SetValue(marker, .035f); marker.enabled = true;
                    ++_request; while (_completed != _request) yield return null;
                    Check((float)ratio.GetValue(marker)! == .035f,
                        "the actual prior hover raster retains its explicit original depth reference");
                    priorPixels = Read(full, true, 600 + reuse);
                    bool reacquired=true;
                    foreach(Image background in full.Backgrounds)
                    {
                        MenuDepthGraphic helper=background.GetComponent<MenuDepthGraphic>();
                        reacquired&=helper!=null&&sources.Contains(helper);
                    }
                    Check(reacquired,"active native card backgrounds rejoin capture after a same-state marker reacquire without another enable");
                }
                finally { ratio.SetValue(marker, configured); }
                float priorDisplacement = Vector2.Scale(White(priorPixels).Center - White(flat).Center,
                    new Vector2(first.width, first.height)).magnitude;
                File.AppendAllText(Path.Combine(_output, "native-hover-relief.csv"),
                    _name + "," + reuse + ",new=" + displacement + ",old=" + priorDisplacement + "\n");
                Object.DestroyImmediate(priorPixels);
                Check(displacement > priorDisplacement + .35f,
                    "native character hover is visibly stronger than the prior 3.5-percent plane");
                Check(full.Rect.localPosition == nativePosition && full.Rect.localScale == nativeScale,
                    "native hover capture restores accepted native placement and physical scale");
                Object.DestroyImmediate(first); Object.DestroyImmediate(flat); marker.enabled = true;
                row.ToggleFullCardPreview(false);
                // Native ObjectPool and TooltipOnWindow return the full face to
                // this exact live pooled row. The campaign pool is not started.
                full.Rect.SetParent(row.transform, false); full.Rect.localScale = Vector3.one;
                mini.Rect.gameObject.SetActive(false);
                ++_request; while (_completed != _request) yield return null;
                Check(!marker.IsReady, "native unhover withdraws the pooled original card capture offer");
            }
            Check(MenuDepthDriver.RootCensuses == censuses && MenuDepthDriver.BehaviourVisits == visits,
                "native pooled hover requests never repeat a whole menu hierarchy census");
            rowGo.SetActive(false); _hoverScene = null;
        }
    }
}
