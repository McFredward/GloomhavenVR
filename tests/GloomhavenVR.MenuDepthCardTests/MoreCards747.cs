using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Code.State;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using HarmonyLib;
using Script.GUI.SMNavigation;
using Script.GUI.SMNavigation.States.CampaignMapStates;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthCardProgram
{
    private static bool _moreGamepad747;

    [HarmonyPatch(typeof(InputManager), "get_GamePadInUse")]
    private static class MoreCardsInputPort747
    {
        [HarmonyPrefix] private static bool Before(ref bool __result) { __result = _moreGamepad747; return false; }
    }

    [HarmonyPatch]
    private static class MoreCardsPorts747
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            // Keep the original pointer methods, native preview activation,
            // added/destroyed Canvas, BlockRaycasts and final event dispatch.
            // Campaign/navigation bootstrap and artwork effects are outside this fixture.
            foreach (string name in new[] { "Awake", "OnDisable", "OnDestroy", "SetBackground" })
                yield return AccessTools.Method(typeof(UILevelUpCard), name);
            yield return AccessTools.Method(typeof(FullAbilityCard), "Highlight");
            yield return AccessTools.Method(typeof(FullAbilityCard), "ToggleBurningAnimation");
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(AbilityCardUI)))
                if (method.Name == "SetDisplayMode") yield return method;
        }
        [HarmonyPrefix] private static bool Before() => false;
    }

    [HarmonyPatch(typeof(RectTransformExtensions), nameof(RectTransformExtensions.DeltaWorldPositionToFitTheScreen),
        new[] { typeof(RectTransform), typeof(Camera), typeof(float), typeof(float) })]
    private static class MoreCardsFitPort747
    {
        // Existing production CutWorldFitCameraMarginXY contract: a converted
        // card has no desktop-screen fit delta. This is not the new placement.
        [HarmonyPrefix] private static bool Before(ref Vector3 __result) { __result = Vector3.zero; return false; }
    }

    private sealed class MoreCardsState747 : IState
    {
        public void Enter<TPayload>(IStateProvider stateProvider, TPayload payload = null!) where TPayload : class { }
        public void Exit<TPayload>(IStateProvider stateProvider, TPayload payload = null!) where TPayload : class { }
    }

    private static long PixelDifference747(Texture2D actual, Texture2D reference)
    {
        Color32[] a = actual.GetPixels32(), b = reference.GetPixels32();
        long error = 0;
        for (int i = 0; i < a.Length; i++)
            error += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
        return error;
    }

    private static int WhitePixels747(Texture2D pixels)
    {
        int count = 0;
        foreach (Color32 pixel in pixels.GetPixels32())
            if (pixel.r > 180 && pixel.g > 180 && pixel.b > 180) count++;
        return count;
    }

    private static int MaximumChannelDifference747(Texture2D actual, Texture2D reference)
    {
        Color32[] a = actual.GetPixels32(), b = reference.GetPixels32(); int maximum = 0;
        for (int i = 0; i < a.Length; i++)
            maximum = Math.Max(maximum, Math.Max(Math.Abs(a[i].r - b[i].r),
                Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b))));
        return maximum;
    }

    private sealed partial class Scene
    {
        private Rect HeadBounds747(Transform space, Vector3 min, Vector3 max, Vector3 depth)
        {
            float x0 = float.PositiveInfinity, y0 = float.PositiveInfinity;
            float x1 = float.NegativeInfinity, y1 = float.NegativeInfinity;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 point = _head.WorldToViewportPoint(space.TransformPoint(new Vector3(
                    (corner & 1) == 0 ? min.x : max.x, (corner & 2) == 0 ? min.y : max.y, min.z)) + depth);
                Check(point.z > _head.nearClipPlane, "complete browse-card geometry remains in front of the final head camera");
                x0 = Mathf.Min(x0, point.x * _final.width); x1 = Mathf.Max(x1, point.x * _final.width);
                y0 = Mathf.Min(y0, point.y * _final.height); y1 = Mathf.Max(y1, point.y * _final.height);
            }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        private void CompleteInk747(Texture2D pixels, Card card, Image left, Image right)
        {
            // Independent physical specification: Full cards protrude by 10.4%
            // of their world width. Do not read the production plane/its mesh.
            Vector3 depth = -_root.forward * (.104f * card.Rect.rect.width
                * card.Rect.TransformVector(Vector3.right).magnitude);
            Rect frame = HeadBounds747(card.Rect, card.Rect.rect.min, card.Rect.rect.max, depth);
            Check(frame.xMin > 8 && frame.xMax < pixels.width - 8 && frame.yMin > 8 && frame.yMax < pixels.height - 8,
                "complete original browse-card frame stays inside actual final eye pixels with independent margins");
            Color32[] ink = pixels.GetPixels32();
            foreach (Image witness in new[] { left, right })
            {
                Vector3 center = _head.WorldToViewportPoint(witness.rectTransform.TransformPoint(witness.rectTransform.rect.center) + depth);
                int cx = Mathf.RoundToInt(center.x * pixels.width), cy = Mathf.RoundToInt(center.y * pixels.height);
                int white = 0;
                for (int y = cy - 2; y <= cy + 2; y++) for (int x = cx - 2; x <= cx + 2; x++)
                {
                    Color32 pixel = ink[y * pixels.width + x];
                    if (pixel.r > 180 && pixel.g > 180 && pixel.b > 180) white++;
                }
                File.AppendAllText(Path.Combine(_output, "independent-edge-ink.csv"), witness.name + ",x=" + cx + ",y=" + cy + ",white=" + white + "\n");
                Check(white >= 23, "both independently projected printed card edges remain visible in actual first and settled head pixels");
            }
            Bounds title = card.Title.textBounds;
            Rect titleFrame = HeadBounds747(card.Title.transform, title.min, title.max, depth);
            Check(titleFrame.xMin > 8 && titleFrame.xMax < pixels.width - 8 && titleFrame.yMin > 8 && titleFrame.yMax < pixels.height - 8,
                "the complete original card title is inside the head image before testing its ink");
            int minX = pixels.width, maxX = -1;
            for (int y = Mathf.FloorToInt(titleFrame.yMin) - 2; y <= Mathf.CeilToInt(titleFrame.yMax) + 2; y++)
                for (int x = Mathf.FloorToInt(titleFrame.xMin) - 2; x <= Mathf.CeilToInt(titleFrame.xMax) + 2; x++)
                {
                    Color32 pixel = ink[y * pixels.width + x];
                    if (pixel.r > 180 && pixel.g > 180 && pixel.b > 180) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
                }
            Check(minX <= titleFrame.xMin + 5 && maxX >= titleFrame.xMax - 5,
                "actual first and settled head ink spans the complete independently projected title extent");
        }

        internal IEnumerator ProveMoreCards747()
        {
            File.WriteAllText(Path.Combine(_output, "more-cards-scope.txt"),
                "Actual GH.Runtime UILevelUpCard.MouseEnter/MouseExit, AbilityCardUI.ToggleFullCardPreview, original transient Canvas, " +
                "BlockRaycasts and pointer UnityEvents execute with production source-bound TooltipOnWindow placement/raise/restore " +
                "and MenuDepthDisplay final head pixels. Native gameplay/artwork/bootstrap are explicit ports; native typed cards " +
                "use declared graphic/text witnesses. Session navigation is browse-only and no UILevelUpWindow is opened. " +
                "Production Canvas adoption and kept-overlay ownership execute. Converted owner/order-ladder, pointer/gamepad input " +
                "and original screen-fit cut are declared service ports. Actual ObjectPool.SpawnCard/RecycleCard " +
                "execute through the audited four-call editor LayoutRebuilder API port. " +
                "First and three settled frames precede the independently requested existing tooltip-kernel reference. " +
                "No native game art, D3D11, HMD, transport or exact user-frame timing claim.\n");
            foreach (Card known in _cards) known.Rect.gameObject.SetActive(false);
            // Native pixel/world units are equal in this controlled source window.
            // The final glass, eye and card/window proportions scale together.
            _root.localScale = Vector3.one;
            _glass.sizeDelta = _root.rect.size;
            _head.nearClipPlane = _ui.nearClipPlane = 10f;
            _head.farClipPlane = _ui.farClipPlane = 5000f;
            _canvas.overrideSorting = true; _canvas.sortingOrder = 100;
            ConvertedPanel panel = CanvasConversion.ActivePanels[0];
            panel.HostRect = _root; panel.HostGo = _root.gameObject; panel.HostCanvas = _canvas; panel.OrderListed = true;
            var navigation = Go("native browse navigation input").AddComponent<UINavigation>();
            var state = (NavigationStateMachine)FormatterServices.GetUninitializedObject(typeof(NavigationStateMachine));
            var browseState = new MoreCardsState747(); var revealState = new MoreCardsState747();
            Field(state, "_states", new Dictionary<Type, Dictionary<Enum, IState>> {
                [typeof(CampaignMapStateTag)] = new() { [CampaignMapStateTag.ShowingCardLevelUp] = revealState } });
            var history = new List<IState> { browseState }; Field(state, "_stateHistory", history);
            Field(navigation, "<StateMachine>k__BackingField", state);
            var manager = Go("native UI camera input").AddComponent<UIManager>(); Field(manager, "uiCamera", _ui);

            var inventory = (RectTransform)Go("native Further Ability Cards inventory", _root).transform;
            inventory.sizeDelta = new Vector2(240, 760); inventory.anchoredPosition = new Vector2(180, 0);
            var viewport = (RectTransform)Go("native inventory scroll viewport", inventory).transform;
            viewport.sizeDelta = inventory.sizeDelta; viewport.gameObject.AddComponent<RectMask2D>();
            var list = (RectTransform)Go("native pooled UILevelUpCard list", viewport).transform;
            var entryGo = Go("original locked browse row", list); entryGo.SetActive(false);
            var entry = entryGo.AddComponent<UILevelUpCard>(); entry.enabled = false;
            var button = entryGo.AddComponent<ExtendedButton>(); button.interactable = false;
            Field(entry, "button", button); Field(entry, "_fullCardPrewiewOffset", new Vector3(-60f, -45f));
            var rowGo = Go("original AbilityCardUI in inventory row", entryGo.transform); rowGo.SetActive(false);
            var row = rowGo.AddComponent<AbilityCardUI>(); row.enabled = false;
            row.CardID = 74700;
            var full = new Card(typeof(FullAbilityCard), (RectTransform)rowGo.transform, 0);
            var mini = new Card(typeof(MiniAbilityCard), (RectTransform)rowGo.transform, 1);
            full.Rect.gameObject.layer = mini.Rect.gameObject.layer = 5;
            full.Owner.enabled = mini.Owner.enabled = false;
            mini.Rect.sizeDelta = new Vector2(220, 50); mini.Title.gameObject.SetActive(false);
            foreach (Image background in mini.Backgrounds) background.rectTransform.sizeDelta = mini.Rect.sizeDelta;
            Field(row, "fullAbilityCard", full.Owner); Field(row, "miniAbilityCard", mini.Owner);
            Field(full.Owner, "_enhancementElements", new CardEnhancementElements());
            Field(row, "mode", CardHandMode.DeckSelection); Field(row, "currentFullCardSpacing", 33f);
            Field(row, "previewCardHeight", 55f); row.MarkAsInFurtherAbilityPanel(true);
            Field(entry, "abilityCard", row);
            rowGo.SetActive(true); entryGo.SetActive(true);
            full.Rect.gameObject.SetActive(false); mini.Rect.gameObject.SetActive(true);
            int enterEvents = 0, exitEvents = 0;
            entry.OnMouseEnter.AddListener(() => enterEvents++); entry.OnMouseExit.AddListener(() => exitEvents++);
            // A later opaque inventory sibling independently witnesses the native
            // order-10 preview drawing behind its order-100 converted owner.
            var backing = Image(inventory, "original inventory strip backing", inventory.sizeDelta, Vector2.zero);
            backing.color = new Color(.1f, .1f, .1f, 1f); backing.raycastTarget = false;
            var left = Image(full.Rect, "original card left ink witness", new Vector2(48, 32), new Vector2(-106, -160)); left.color = Color.white;
            var right = Image(full.Rect, "original card right ink witness", new Vector2(48, 32), new Vector2(106, -160)); right.color = Color.white;
            var poolGo = Go("original browse-card native pool");
            var pool = poolGo.AddComponent<ObjectPool>(); ObjectPool.instance = pool;
            Field(pool, "cardPools", new List<ObjectPool.CardPool> {
                new(row.CardID, ObjectPool.ECardType.Ability, new List<GameObject> { rowGo, rowGo }) });
            int censuses = MenuDepthDriver.RootCensuses, visits = MenuDepthDriver.BehaviourVisits;
            try
            {
                for (int guard = 0; guard < 7; guard++)
                {
                    GloomhavenVR.Core.VRSession.IsRunning = guard != 0;
                    WorldUIConfig.ConversionActive = guard != 1;
                    FlatScreen.ManualScreenActive = guard == 2;
                    panel.IsAlive = guard != 3;
                    if (guard == 4) CanvasConversion.ActivePanels.Clear();
                    button.interactable = guard == 5;
                    history[0] = guard == 6 ? revealState : browseState;
                    entry.MouseEnter();
                    Check(full.Rect.parent == row.transform && TooltipOnWindow.FixtureOutstanding == 0,
                        "excluded native browse contexts preserve the original preview parent");
                    Check(full.Rect.gameObject.activeSelf == (guard < 5),
                        "native browse readiness and reveal-state guards alone decide preview visibility");
                    history[0] = browseState; button.interactable = false;
                    entry.MouseExit();
                    GloomhavenVR.Core.VRSession.IsRunning = WorldUIConfig.ConversionActive = true;
                    FlatScreen.ManualScreenActive = false; panel.IsAlive = true;
                    if (guard == 4) CanvasConversion.ActivePanels.Add(panel);
                    ++_request; while (_completed != _request) yield return null;
                }
                int priorEnters = enterEvents, priorExits = exitEvents;
                for (int reuse = 0; reuse < 6; reuse++)
                {
                    _moreGamepad747 = reuse >= 3;
                    Vector3 nativeOffset = new Vector3(_moreGamepad747 ? -75f : -60f, -45f);
                    Field(entry, "_fullCardPrewiewOffset", nativeOffset);
                    full.Rect.sizeDelta = _moreGamepad747 ? new Vector2(360, 555) : new Vector2(294, 450);
                    float width = full.Rect.rect.width, height = full.Rect.rect.height;
                    full.Backgrounds[0].rectTransform.sizeDelta = new Vector2(width, 80);
                    full.Backgrounds[0].rectTransform.anchoredPosition = new Vector2(0, height * .5f - 40);
                    full.Backgrounds[1].rectTransform.sizeDelta = new Vector2(width, height - 80);
                    left.rectTransform.anchoredPosition = new Vector2(-width * .36f, -height * .36f);
                    right.rectTransform.anchoredPosition = new Vector2(width * .36f, -height * .36f);
                    _root.rotation = reuse < 2 ? Quaternion.identity : Quaternion.Euler(0, reuse % 2 == 0 ? 31f : -27f, 0);
                    _glass.rotation = _root.rotation;
                    Vector3 eye = _glass.TransformPoint(new Vector3(100, 30, -900));
                    _head.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(_glass.position - eye, _glass.up));
                    full.Title.text = reuse % 2 == 0 ? "FIRST BROWSE CARD" : "SECOND BROWSE CARD";
                    rowGo.SetActive(false);
                    GameObject borrowed = ObjectPool.SpawnCard(row.CardID, ObjectPool.ECardType.Ability, entry.transform,
                        resetLocalScale: true, resetToMiddle: true);
                    Check(ReferenceEquals(borrowed, rowGo), "More Cards reuses the exact original native pooled card without cloning its controller");
                    mini.Rect.anchoredPosition = reuse % 2 == 0 ? Vector2.zero : new Vector2(18, -8);
                    entry.MouseEnter();
                    Check(full.Rect.gameObject.activeInHierarchy && enterEvents == priorEnters + reuse + 1,
                        "original browse-only hover activates its full card and native event once");
                    ++_request; while (_completed != _request) yield return null;
                    Texture2D first = Read(full, true, 74700 + reuse * 10);
                    var steadyFrames = new List<Texture2D>();
                    Texture2D? settled = null;
                    for (int frame = 0; frame < 3; frame++)
                    {
                        CanvasConversion.FixtureAdopt(panel, full.Rect.GetComponent<Canvas>());
                        TooltipOnWindow.FixtureTick();
                        ++_request; while (_completed != _request) yield return null;
                        settled = Read(full, true, 74701 + reuse * 10 + frame);
                        steadyFrames.Add(settled);
                    }
                    // Request the existing, independently source-bound tooltip
                    // behavior as the reference only AFTER actual native frames.
                    Vector3 actualOwnerPosition = _root.InverseTransformPoint(full.Rect.position);
                    Quaternion actualOrientation = full.Rect.rotation;
                    float actualWidth = full.Rect.rect.width * full.Rect.TransformVector(Vector3.right).magnitude;
                    Vector3 miniInWindow = _root.InverseTransformPoint(mini.Rect.position);
                    Vector3 rowInWindow = _root.InverseTransformPoint(row.transform.position);
                    TooltipOnWindow.PlaceFullCardPreview(row, full.Owner,
                        miniInWindow.x - rowInWindow.x + nativeOffset.x,
                        miniInWindow.y - rowInWindow.y + nativeOffset.y,
                        "reference original Further Ability Cards preview");
                    TooltipOnWindow.FixtureTick();
                    ++_request; while (_completed != _request) yield return null;
                    Texture2D reference = Read(full, false, 74709 + reuse * 10);
                    try
                    {
                        int firstInk = WhitePixels747(first), settledInk = WhitePixels747(settled!), referenceInk = WhitePixels747(reference);
                        long firstError = PixelDifference747(first, reference), settledError = PixelDifference747(settled!, reference);
                        int firstMaximum = MaximumChannelDifference747(first, reference), settledMaximum = MaximumChannelDifference747(settled!, reference);
                        float ownerDelta = Vector3.Distance(actualOwnerPosition, _root.InverseTransformPoint(full.Rect.position));
                        Vector3 actualProjected = _head.WorldToViewportPoint(_root.TransformPoint(actualOwnerPosition));
                        Vector3 referenceProjected = _head.WorldToViewportPoint(full.Rect.position);
                        float projectedDelta = Vector2.Scale(new Vector2(actualProjected.x - referenceProjected.x,
                            actualProjected.y - referenceProjected.y), new Vector2(first.width, first.height)).magnitude;
                        File.AppendAllText(Path.Combine(_output, "more-cards-first-steady.csv"), reuse + ",firstError=" + firstError + ",settledError=" + settledError
                            + ",firstMaxChannel=" + firstMaximum + ",settledMaxChannel=" + settledMaximum
                            + ",ownerNativePixelDelta=" + ownerDelta.ToString("R") + ",headPixelDelta=" + projectedDelta.ToString("R")
                            + ",firstInk=" + firstInk + ",settledInk=" + settledInk + ",referenceInk=" + referenceInk
                            + ",parent=" + full.Rect.parent.name + ",order=" + full.Rect.GetComponent<Canvas>().sortingOrder + "\n");
                        Check(referenceInk > 100, "independent complete browse-card reference contains actual title and both edge ink witnesses");
                        // Point subtraction and inverse-vector evaluation differ
                        // by float rounding after a rotated native pool roundtrip.
                        // Admit at most 128 one-LSB channel units in the independent
                        // reference; actual first/steady images stay bit-identical.
                        Check(firstError <= 128 && settledError <= 128
                            && firstMaximum <= 1 && settledMaximum <= 1,
                            "browse-only More Cards keeps the complete original preview visible beyond the inventory strip in first and settled head pixels");
                        Check(ownerDelta <= .0001f && projectedDelta <= .001f
                            && Quaternion.Angle(actualOrientation, full.Rect.rotation) < .001f
                            && Mathf.Abs(actualWidth - full.Rect.rect.width * full.Rect.TransformVector(Vector3.right).magnitude) <= .0001f,
                            "independent point-based browse reference preserves owner geometry orientation width and final head projection within float roundoff");
                        CompleteInk747(first, full, left, right);
                        foreach (Texture2D steady in steadyFrames)
                        {
                            Check(PixelDifference747(first, steady) == 0
                                && PixelDifference747(steady, reference) <= 128
                                && MaximumChannelDifference747(steady, reference) <= 1,
                                "browse-only More Cards keeps the complete original preview visible beyond the inventory strip in first and settled head pixels");
                            CompleteInk747(steady, full, left, right);
                        }
                        Check(full.Rect.parent == _root && full.Rect.GetComponent<Canvas>().sortingOrder == 115,
                            "native browse preview uses the existing owner raise and top-of-slot ordering");
                    }
                    finally
                    {
                        Object.DestroyImmediate(first); Object.DestroyImmediate(reference);
                        foreach (Texture2D steady in steadyFrames) Object.DestroyImmediate(steady);
                    }
                    entry.MouseExit();
                    Check(!full.Rect.gameObject.activeSelf && full.Rect.parent == row.transform && exitEvents == priorExits + reuse + 1
                        && TooltipOnWindow.FixtureOutstanding == 0,
                        "original browse unhover restores the exact pooled home without stranded raises or changed native callbacks");
                    ++_request; while (_completed != _request) yield return null;
                    Check(full.Rect.GetComponent<Canvas>() == null, "native browse hide retires its transient Canvas before reuse");
                    ObjectPool.RecycleCard(row.CardID, ObjectPool.ECardType.Ability, rowGo);
                    Check(!rowGo.activeSelf && row.transform.parent == poolGo.transform && full.Rect.parent == row.transform,
                        "native recycle accepts the restored Full and returns the original browse card to its native pool");
                }
                Check(MenuDepthDriver.RootCensuses == censuses && MenuDepthDriver.BehaviourVisits == visits,
                    "browse preview reuse never adds a per-hover whole-window hierarchy census");
            }
            finally
            {
                GloomhavenVR.Core.VRSession.IsRunning = WorldUIConfig.ConversionActive = true;
                FlatScreen.ManualScreenActive = false; panel.IsAlive = true; _moreGamepad747 = false;
                entryGo.SetActive(false); TooltipOnWindow.FixtureTick(); ObjectPool.instance = null;
            }
        }
    }
}
