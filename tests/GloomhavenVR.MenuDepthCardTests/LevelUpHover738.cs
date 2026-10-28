using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MenuDepthCardProgram
{
    [HarmonyPatch]
    private static class LevelUpHoverPorts738
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            // Original holder, highlighter and SpawnCard/RecycleCard execute.
            // Only campaign/asset bootstrap and card decoration are fixture ports.
            foreach (string name in new[] { "Awake", "Start" })
                yield return AccessTools.Method(typeof(ObjectPool), name);
            yield return AccessTools.Method(typeof(UILevelUpCardHighlighter), "Awake");
            yield return AccessTools.Method(typeof(AbilityCardUI), "Init", new[] { typeof(CAbilityCard), typeof(bool) });
            yield return AccessTools.Method(typeof(AbilityCardUI), "Show");
            yield return AccessTools.Method(typeof(AbilityCardUI), "OnReturnedToPool");
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(FullAbilityCard)))
                if (method.Name == "UpdateView") yield return method;
        }
        [HarmonyPrefix] private static bool Before() => false;
    }

    private sealed partial class Scene
    {
        internal IEnumerator ProveLevelUpHover738()
        {
            File.WriteAllText(Path.Combine(_output, "level-up-scope.txt"),
                "Actual UILevelUpCardHighlighter.HighlightCard, UILevelUpCardHolder.PlaceNewCard/Hide, " +
                "ObjectPool.SpawnCard/RecycleCard, typed cards, TMP/uGUI, production observer, " +
                "MenuDepthDisplay.RenderHead and final eye pixels. Original campaign Init/Show/art decoration " +
                "and pool-return gameplay cleanup are explicit ports. No actual native game art/D3D11/HMD claim.\n");
            foreach (Card known in _cards) known.Rect.gameObject.SetActive(false);
            var poolGo = Go("original native ObjectPool");
            var pool = poolGo.AddComponent<ObjectPool>(); ObjectPool.instance = pool;
            var holderGo = Go("original UILevelUpCardHolder", _root); holderGo.SetActive(false);
            var holder = holderGo.AddComponent<UILevelUpCardHolder>();
            var alpha = holderGo.AddComponent<CanvasGroup>(); Field(holder, "canvasGroup", alpha);
            holderGo.SetActive(true);
            var highlight = Go("original UILevelUpCardHighlighter", _root).AddComponent<UILevelUpCardHighlighter>();
            Field(highlight, "cardHolder", holder); Field(highlight, "cardHighlightEffect", Go("original optional card effect", _root));
            var data = new CAbilityCard[2]; var rows = new AbilityCardUI[2]; var cards = new Card[2];
            var pools = new List<ObjectPool.CardPool>();
            for (int i = 0; i < rows.Length; i++)
            {
                int id = 73800 + i;
                data[i] = new CAbilityCard();
                AccessTools.Field(typeof(CBaseCard), "<ID>k__BackingField").SetValue(data[i], id);
                var rowGo = Go("native pool card " + id, poolGo.transform); rowGo.SetActive(false);
                rows[i] = rowGo.AddComponent<AbilityCardUI>(); rows[i].enabled = false; rows[i].CardID = id;
                cards[i] = new Card(typeof(FullAbilityCard), (RectTransform)rowGo.transform, i);
                cards[i].Rect.anchoredPosition = Vector2.zero; cards[i].Rect.gameObject.layer = 5;
                cards[i].Title.text = i == 0 ? "FIRST LEVEL CARD" : "SECOND LEVEL CARD";
                cards[i].Rect.gameObject.SetActive(true);
                Field(cards[i].Owner, "_enhancementElements", new CardEnhancementElements());
                Field(rows[i], "fullAbilityCard", cards[i].Owner);
                // Keep a real native template entry so GetCardInstance uses the
                // original pooled instance, rather than instantiating a new fake.
                pools.Add(new ObjectPool.CardPool(id, ObjectPool.ECardType.Ability,
                    new List<GameObject> { rowGo, rowGo }));
            }
            Field(pool, "cardPools", pools);
            ConvertedPanel panel = CanvasConversion.ActivePanels[0];
            var physical = Go("unrelated physical preview outside every converted window");
            physical.transform.rotation = Quaternion.Euler(0f, -47f, 0f);
            int shownCallbacks = 0;
            try
            {
                _root.rotation = Quaternion.Euler(0f, 47f, 0f); _glass.rotation = _root.rotation;
                Vector3 eye = _glass.TransformPoint(new Vector3(.10f, .03f, -.90f));
                _head.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(_glass.position - eye, _glass.up));
                // Original pool requests preserve world rotation. The UI-only
                // correction must not escape into flat/manual/retired states.
                for (int guard = 0; guard < 6; guard++)
                {
                    WorldUIConfig.ConversionActive = guard != 0;
                    VRSession.IsRunning = guard != 1;
                    FlatScreen.ManualScreenActive = guard == 2;
                    panel.IsAlive = guard != 3;
                    if (guard == 4) CanvasConversion.ActivePanels.Clear();
                    if (guard == 5) holder.transform.SetParent(physical.transform, false);
                    holder.PlaceNewCard(data[0], immediately: true, () => shownCallbacks++);
                    Check(Quaternion.Angle(rows[0].transform.rotation, Quaternion.identity) < .01f
                        && Quaternion.Angle(rows[0].transform.localRotation, Quaternion.identity) > 30f,
                        "non-window manual flat retired and VR-off holders retain original native pool rotation");
                    holder.Hide();
                    WorldUIConfig.ConversionActive = VRSession.IsRunning = true;
                    FlatScreen.ManualScreenActive = false; panel.IsAlive = true;
                    if (guard == 4) CanvasConversion.ActivePanels.Add(panel);
                    if (guard == 5) holder.transform.SetParent(_root, false);
                }
                Check(shownCallbacks == 6, "native immediate-show callbacks survive every excluded hover route exactly once");

                holder.PlaceNewCard(data[0], immediately: true, () =>
                {
                    rows[0].transform.SetParent(physical.transform, false);
                    rows[0].transform.localRotation = Quaternion.Euler(0f, 23f, 0f);
                });
                Check(rows[0].transform.parent == physical.transform
                    && Quaternion.Angle(rows[0].transform.localRotation, Quaternion.Euler(0f, 23f, 0f)) < .01f,
                    "an original shown callback retains its deliberate new card parent and orientation");
                holder.Hide();

                holder.PlaceNewCard(data[0], immediately: false);
                Check(Quaternion.Angle(rows[0].transform.rotation, Quaternion.identity) < .01f
                    && AccessTools.Field(typeof(UILevelUpCardHolder), "animationFade").GetValue(holder) != null,
                    "the original non-immediate won-card reveal keeps its native rotation and fade animation");
                holder.Hide();

                for (int reuse = 0; reuse < 12; reuse++)
                {
                    int index = reuse % 2;
                    _root.rotation = Quaternion.Euler(0f, reuse % 2 == 0 ? 47f : -36f, 0f); _glass.rotation = _root.rotation;
                    eye = _glass.TransformPoint(new Vector3(.10f, .03f, -.90f));
                    _head.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(_glass.position - eye, _glass.up));
                    // Two genuine native hover requests before any render model a
                    // laser rapidly sweeping past one card to another in one frame.
                    highlight.HighlightCard(data[1 - index], showEffect: false);
                    highlight.HighlightCard(data[index], showEffect: false);
                    Check(ReferenceEquals(holder.Card, rows[index]) && !rows[1 - index].gameObject.activeSelf,
                        "rapid original level-up hover swaps retain exactly the last selected pooled card");
                    Vector3 position = rows[index].transform.localPosition, scale = rows[index].transform.localScale;
                    Check(position == Vector3.zero && scale == Vector3.one && alpha.alpha == 1f,
                        "native immediate hover retains original position scale and visibility without an added delay");
                    _rightEye = (reuse & 1) != 0;
                    ++_request; while (_completed != _request) yield return null;
                    Texture2D first = Read(cards[index], true, 73800 + reuse);
                    // Independent native UI intent: the original card root is
                    // unrotated in its own window, as it was in the flat pool.
                    // This is specified directly, not read from the observer.
                    rows[index].transform.localRotation = Quaternion.identity;
                    ++_request; while (_completed != _request) yield return null;
                    Texture2D settled = Read(cards[index], false, 73800 + reuse);
                    try
                    {
                        Color32[] actual = first.GetPixels32(), wanted = settled.GetPixels32();
                        long error = 0; int changed = 0;
                        for (int p = 0; p < wanted.Length; p++)
                        {
                            int delta = Math.Abs(actual[p].r - wanted[p].r) + Math.Abs(actual[p].g - wanted[p].g) + Math.Abs(actual[p].b - wanted[p].b);
                            error += delta; if (delta > 12) changed++;
                        }
                        White(first);
                        File.AppendAllText(Path.Combine(_output, "first-hover-pixels.csv"), reuse + ",changed=" + changed + ",error=" + error + "\n");
                        Check(changed == 0,
                            "the first native level-up hover raster already matches its independently aligned window plane");
                        Check(rows[index].transform.localPosition == position && rows[index].transform.localScale == scale,
                            "first-hover orientation leaves original pool placement and physical card size intact");
                    }
                    finally { Object.DestroyImmediate(first); Object.DestroyImmediate(settled); }
                    highlight.UnhighlightCard();
                    Check(holder.Card == null && !rows[index].gameObject.activeSelf
                        && rows[index].transform.parent == poolGo.transform
                        && Quaternion.Angle(rows[index].transform.localRotation, Quaternion.identity) < .01f,
                        "original unhover returns the same card and authored orientation to its native pool");
                }
            }
            finally
            {
                WorldUIConfig.ConversionActive = VRSession.IsRunning = true;
                FlatScreen.ManualScreenActive = false; panel.IsAlive = true;
                holder.Hide(); ObjectPool.instance = null;
            }
        }
    }
}
