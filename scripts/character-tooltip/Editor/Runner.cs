using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class CharacterTooltipRunner
{
    [Serializable] public sealed class Case { public string name, dll, expected; public bool nativeFlat; }
    [Serializable] public sealed class Manifest { public string result, evidence; public Case[] cases; }
    private static Manifest manifest;
    private static StreamWriter writer;
    private static IEnumerator routine;
    private static int index, assertions;
    private static bool passed = true;
    private static Harmony harmony;
    private static Assembly mod;
    private static int classifications;
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(args[Array.IndexOf(args, "-characterTooltipManifest") + 1]));
        writer = new StreamWriter(manifest.result);
        writer.WriteLine("Unity " + Application.unityVersion + "; " + SystemInfo.graphicsDeviceName + "; " + SystemInfo.graphicsDeviceType);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (index == manifest.cases.Length)
        { writer.Dispose(); EditorApplication.update -= Tick; EditorApplication.Exit(passed ? 0 : 1); return; }
        Case item = manifest.cases[index];
        try
        {
            if (routine == null)
            {
                assertions = 0; classifications = 0; mod = Assembly.LoadFile(item.dll);
                harmony = new Harmony("ghvr.character.tooltip." + item.name);
                foreach (Type type in new[] { typeof(UIAdventurePartyAssemblyCharacterDisplay), typeof(UICampaignAssemblyCharacterInformation),
                    typeof(UICharacterCreatorWindow), typeof(UICharacterCreatorClassRoster), typeof(UIPersonalQuestState),
                    typeof(UIManager), typeof(TextLocalizedListener), typeof(UITooltip) })
                    foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
                        if (method.Name == "Awake" || method.Name == "Start" || method.Name == "OnEnable"
                            || method.Name == "OnDisable" || method.Name == "OnDestroy")
                            harmony.Patch(method, prefix: new HarmonyMethod(typeof(CharacterTooltipRunner), nameof(DormantLifecycle)));
                Set(mod.GetType("GloomhavenVR.Core.VRSession"), "IsRunning", true);
                harmony.CreateClassProcessor(mod.GetType("GloomhavenVR.WorldUI.Patches.TooltipWindowPatches")).Patch();
                harmony.Patch(AccessTools.Method(mod.GetType("GloomhavenVR.WorldUI.CharacterTooltipPlacement"), "IsSharedHint"),
                    postfix: new HarmonyMethod(typeof(CharacterTooltipRunner), nameof(Classified)));
                routine = item.nativeFlat ? RunNativeFlat(Path.Combine(manifest.evidence, item.name))
                    : Run(Path.Combine(manifest.evidence, item.name));
            }
            if (routine.MoveNext()) return;
            if (!string.IsNullOrEmpty(item.expected)) throw new Exception("Negative control escaped: " + item.name);
            writer.WriteLine("PASS " + item.name + ": " + assertions
                + (item.nativeFlat ? " native layout/callback assertions" : " native layout/GPU assertions"));
        }
        catch (Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            if (!string.IsNullOrEmpty(item.expected) && error.Message.Contains(item.expected))
                writer.WriteLine("PASS negative control " + item.name + ": " + error.Message);
            else { passed = false; writer.WriteLine("FAIL " + item.name + ": " + error); }
            Complete();
        }
        if (routine == null) { index++; writer.Flush(); return; }
        // A completed enumerator has not been cleared by the catch path.
        if (routine.Current == Done) { Complete(); index++; writer.Flush(); }
    }
    private static readonly object Done = new object();
    private static bool DormantLifecycle() => false;
    private static bool NativeMouseInput(ref bool __result) { __result = false; return false; }
    private static void Classified() => classifications++;
    private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    private static object Get(object target, string field)
    {
        Type type = target as Type ?? target.GetType();
        return type.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).GetValue(target is Type ? null : target);
    }
    private static void Set(object target, string member, object value)
    {
        Type type = target as Type ?? target.GetType();
        FieldInfo field = type.GetField(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        if (field != null) field.SetValue(target is Type ? null : target, value);
        else type.GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).SetValue(target is Type ? null : target, value, null);
    }
    private static object Call(object target, string member, params object[] args)
    {
        Type type = target as Type ?? target.GetType();
        try { return type.GetMethod(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).Invoke(target is Type ? null : target, args); }
        catch (TargetInvocationException e) { throw e.InnerException ?? e; }
    }
    private static void Complete()
    {
        if (routine is IDisposable disposable) disposable.Dispose(); routine = null;
        try { Call(mod.GetType("GloomhavenVR.WorldUI.TooltipOnWindow"), "Shutdown"); } catch { }
        foreach (GameObject go in Object.FindObjectsOfType<GameObject>()) Object.DestroyImmediate(go);
        harmony.UnpatchSelf();
    }
    private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform; if (parent != null) rect.SetParent(parent, false);
        rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
    }
    private static T Native<T>(GameObject go) where T : Behaviour
    { T value = go.AddComponent<T>(); value.enabled = false; return value; }
    private static Rect BoundsIn(RectTransform host, RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector3 p = host.InverseTransformPoint(corners[0]); float x0 = p.x, x1 = p.x, y0 = p.y, y1 = p.y;
        for (int i = 1; i < 4; i++) { p = host.InverseTransformPoint(corners[i]); x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y); }
        return UnityEngine.Rect.MinMaxRect(x0, y0, x1, y1);
    }
    private static void Right(RectTransform host, RectTransform rect, string message)
    { Rect b = BoundsIn(host, rect); Check(Math.Abs(b.xMin - host.rect.xMax - 12f) < .035f, message); }
    private static IEnumerator Run(string output)
    {
        Directory.CreateDirectory(output);
        Type worldType = mod.GetType("GloomhavenVR.WorldUI.WorldTooltips");
        Type localType = mod.GetType("GloomhavenVR.WorldUI.TooltipOnWindow");
        Type panelType = mod.GetType("GloomhavenVR.WorldUI.ConvertedPanel");
        Type modalType = mod.GetType("GloomhavenVR.WorldUI.ModalFallback");
        var host = Rect("fixed original frame", null, new Vector2(1988, 1080), Vector2.zero);
        Canvas hostCanvas = host.gameObject.AddComponent<Canvas>(); hostCanvas.renderMode = RenderMode.WorldSpace;
        var nativeRoot = Rect("original character window", host, host.sizeDelta, Vector2.zero);
        UIAdventurePartyAssemblyCharacterDisplay party = Native<UIAdventurePartyAssemblyCharacterDisplay>(nativeRoot.gameObject);
        object panel = Activator.CreateInstance(panelType, true);
        Set(panel, "Target", nativeRoot); Set(panel, "HostRect", host); Set(panel, "HostGo", host.gameObject); Set(panel, "HostCanvas", hostCanvas);
        Type recordType = modalType.GetNestedType("WindowPanel", BindingFlags.NonPublic); object record = Activator.CreateInstance(recordType, true); Set(record, "Panel", panel);
        IList converted = (IList)Get(modalType, "Converted"); converted.Add(record);
        var camera = new GameObject("actual tooltip capture camera").AddComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = 1.1f; camera.aspect = 1.6f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        var rt = new RenderTexture(1280, 800, 24); rt.Create(); camera.targetTexture = rt;
        UIManager manager = Native<UIManager>(new GameObject("original UI manager boundary"));
        Set(manager, "uiCamera", camera); Set(typeof(UIManager), "Instance", manager);
        var shared = new GameObject("original shared tooltip", typeof(RectTransform), typeof(Canvas)).GetComponent<RectTransform>();
        Canvas sharedCanvas = shared.GetComponent<Canvas>(); sharedCanvas.renderMode = RenderMode.WorldSpace; sharedCanvas.sortingOrder = 50;
        UITooltip tooltip = Native<UITooltip>(shared.gameObject);
        Image ink = shared.gameObject.AddComponent<Image>(); ink.color = Color.yellow;
        object world = Activator.CreateInstance(worldType, true); Set(world, "_tooltip", tooltip);
        var skinRect = Rect("random name A", nativeRoot, new Vector2(260, 55), new Vector2(600, -90));
        UICharacterSkinSelector skin = Native<UICharacterSkinSelector>(skinRect.gameObject);
        UITextTooltipTarget skinTarget = Native<UITextTooltipTarget>(skinRect.gameObject); Set(skin, "skinTooltip", skinTarget);
        Button button = skinRect.gameObject.AddComponent<Button>(); int clicked = 0; button.onClick.AddListener(() => clicked++);
        var infoRoot = Rect("campaign information", nativeRoot, new Vector2(600, 800), new Vector2(-200, 0));
        UICampaignAssemblyCharacterInformation information = Native<UICampaignAssemblyCharacterInformation>(infoRoot.gameObject);
        Set(party, "information", new List<UICharacterInformation> { information });
        var description = Rect("random name B", nativeRoot, new Vector2(460, 60), new Vector2(-330, 350));
        var descriptionTarget = Native<UITextTooltipTarget>(description.gameObject); Set(information, "descriptionTooltip", descriptionTarget);
        var quest = Rect("native quest print", infoRoot, new Vector2(522, 170), new Vector2(17, -90));
        Native<UIPersonalQuestState>(quest.gameObject); Native<UIPersonalQuest>(quest.gameObject);
        var conceal = Rect("native conceal icon", quest, new Vector2(32, 32), new Vector2(240, 60));
        var concealTarget = Native<UITextTooltipTarget>(conceal.gameObject); Set(information, "personalQuestTooltip", concealTarget);
        var local = Rect("original quest explanation", quest, new Vector2(600, 130.31f), new Vector2(-15, -92));
        local.pivot = new Vector2(1, .5f); local.anchorMin = local.anchorMax = new Vector2(0, 1);
        Image localInk = local.gameObject.AddComponent<Image>(); localInk.color = Color.yellow;
        var questTextRect = Rect("original quest text input", local, new Vector2(580, 120), Vector2.zero);
        Text questText = questTextRect.gameObject.AddComponent<Text>(); questText.text = "ORIGINAL QUEST EXPLANATION";
        questText.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); questText.fontSize = 30; questText.color = Color.white; questText.alignment = TextAnchor.MiddleCenter;
        UILocalTooltip questTooltip = Native<UILocalTooltip>(local.gameObject); Set(questTooltip, "target", quest); Set(questTooltip, "targetOffset", new Vector2(-15, -90));
        var creatorRoot = Rect("original creation window", nativeRoot, new Vector2(700, 900), Vector2.zero);
        Native<UICharacterCreatorWindow>(creatorRoot.gameObject);
        var rosterRoot = Rect("original class roster", creatorRoot, new Vector2(600, 700), new Vector2(-280, 0));
        UICharacterCreatorClassRoster roster = Native<UICharacterCreatorClassRoster>(rosterRoot.gameObject);
        var perks = Rect("same arbitrary label as skin", rosterRoot, new Vector2(400, 60), new Vector2(0, -200));
        UITextTooltipTarget perksTarget = Native<UITextTooltipTarget>(perks.gameObject);
        TextLocalizedListener perksInformation = Native<TextLocalizedListener>(perks.gameObject); Set(roster, "information", perksInformation);
        var ordinary = Rect(skinRect.name, nativeRoot, new Vector2(300, 50), new Vector2(600, 250));
        Native<UITextTooltipTarget>(ordinary.gameObject);
        Vector2 fixedSize = host.sizeDelta; Transform originalLocalParent = local.parent; Vector2 originalPivot = local.pivot;
        var roles = new[] { skinRect, description, conceal, perks };
        foreach (float yaw in new[] { 0f, 37f, -51f })
        foreach (float scale in new[] { .0005f, .001f })
        {
            host.SetPositionAndRotation(new Vector3(.1f, .2f, 1.2f), Quaternion.Euler(0, yaw, 0)); host.localScale = Vector3.one * scale;
            foreach (RectTransform anchor in roles)
            {
                Set(tooltip, "m_AnchorToTarget", anchor); Call(world, "ResolveHostOwner", true, false);
                Check((bool)Get(world, "_characterHintOnRight"), anchor == perks ? "native creation perks hint uses right lane" : "native declared character hint is classified exactly");
                shared.rotation = host.rotation; shared.localScale = host.lossyScale; shared.sizeDelta = new Vector2(300, 180);
                shared.position = (Vector3)Call(world, "ResolveMenuFrameCenter", host, host.rotation, 150f * scale, 90f * scale);
                Right(host, shared, "native skin hint clears the fixed right edge");
                Vector3 first = shared.position; Call(world, "ResolveHostOwner", false, true);
                Check((bool)Get(world, "_characterHintOnRight"), "the character side decision survives native tooltip fade grace");
                Check(Vector3.Distance(first, (Vector3)Call(world, "ResolveMenuFrameCenter", host, host.rotation, 150f * scale, 90f * scale)) < 1e-6f,
                    "fading character hint remains at the exact same right-side seat");
                int before = classifications;
                for (int i = 0; i < 200; i++) Call(world, "ResolveHostOwner", true, false);
                Check(classifications == before, "warm owner ticks never repeat character hierarchy classification");
                Check(host.sizeDelta == fixedSize, "character hints cannot resize the original window frame");
            }
            Set(tooltip, "m_AnchorToTarget", ordinary); Call(world, "ResolveHostOwner", true, false);
            Check(!(bool)Get(world, "_characterHintOnRight"), "unrelated same-name tooltip retains ordinary inside-window placement");
            Vector3 ordinaryCenter = (Vector3)Call(world, "ResolveMenuFrameCenter", host, host.rotation, 150f * scale, 90f * scale);
            Check(host.InverseTransformPoint(ordinaryCenter).x < host.rect.xMax, "unrelated same-name tooltip retains ordinary inside-window placement");
            // Execute native Show -> RefreshPosition and the unchanged production
            // Harmony postfix/cut. No test-side call replaces Settle or its raise.
            local.SetParent(originalLocalParent, false); local.sizeDelta = new Vector2(600, 130.31f); local.pivot = originalPivot;
            questTooltip.Show(); Canvas.ForceUpdateCanvases();
            Right(host, local, "the original personal quest explanation clears the fixed right edge");
            Check((bool)Call(mod.GetType("GloomhavenVR.WorldUI.MenuDepth.MenuDepthSources"), "HasContent", host),
                "converted quest annotation retains head capture without a ready character source");
            Check(local.parent == nativeRoot, "the original local quest tooltip retains the existing raise/restore lifecycle");
            Check(host.sizeDelta == fixedSize && local.pivot == originalPivot, "quest placement preserves fixed window size and native tooltip pivot");
            local.sizeDelta = new Vector2(720, 350); Canvas.ForceUpdateCanvases(); Call(localType, "TickRaises");
            Right(host, local, "late quest layout retains the fixed right edge");
            Check(host.sizeDelta == fixedSize, "late quest layout cannot enlarge the original window");
            questTooltip.Hide(); Call(localType, "TickRaises");
            Check(local.parent == originalLocalParent, "native quest hide returns the original widget to its original parent");
            local.gameObject.SetActive(true);
        }
        // Actual native world-canvas pixels: the outboard hint remains visible
        // with original size/content; no independent window/frame is added.
        host.SetPositionAndRotation(new Vector3(0, 0, 1.2f), Quaternion.identity); host.localScale = Vector3.one * .001f;
        shared.rotation = host.rotation; shared.localScale = host.lossyScale; shared.sizeDelta = new Vector2(400, 220);
        Set(tooltip, "m_AnchorToTarget", skinRect); Call(world, "ResolveHostOwner", true, false);
        shared.position = (Vector3)Call(world, "ResolveMenuFrameCenter", host, host.rotation, .2f, .11f);
        local.gameObject.SetActive(false); Canvas.ForceUpdateCanvases(); camera.Render();
        RenderTexture.active = rt; var pixels = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        pixels.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); pixels.Apply(); RenderTexture.active = null;
        Color32[] colors = pixels.GetPixels32(); int count = 0, minimum = rt.width;
        for (int y = 0; y < rt.height; y++) for (int x = 0; x < rt.width; x++)
        { Color32 c = colors[y * rt.width + x]; if (c.r > 180 && c.g > 180 && c.b < 80) { count++; minimum = Math.Min(minimum, x); } }
        Check(count > 200, "the original right-side tooltip produces actual visible world-canvas pixels");
        Check(minimum > camera.WorldToViewportPoint(host.TransformPoint(new Vector3(host.rect.xMax, 0))).x * rt.width,
            "rendered character tooltip ink lies entirely to the right of the fixed host");
        File.WriteAllBytes(Path.Combine(output, "actual-right-side-tooltip.png"), pixels.EncodeToPNG()); Object.DestroyImmediate(pixels);
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.name = "opaque live-character volume input";
        body.transform.position = new Vector3(-.35f, 0, 1.08f); body.transform.localScale = new Vector3(.55f, 1.0f, .24f);
        var bodyMaterial = new Material(Shader.Find("Unlit/Color")); bodyMaterial.color = Color.blue; body.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
        shared.gameObject.SetActive(false); local.SetParent(originalLocalParent, false); local.sizeDelta = new Vector2(600, 162.63f);
        questTooltip.Show(); Canvas.ForceUpdateCanvases(); Call(localType, "TickRaises"); camera.Render();
        RenderTexture.active = rt; pixels = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        pixels.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); pixels.Apply(); RenderTexture.active = null;
        colors = pixels.GetPixels32(); int bodyPixels = 0, questPixels = 0, textPixels = 0;
        int rightEdge = Mathf.CeilToInt(camera.WorldToViewportPoint(host.TransformPoint(new Vector3(host.rect.xMax, 0))).x * rt.width);
        for (int y = 0; y < rt.height; y++) for (int x = 0; x < rt.width; x++)
        { Color32 c = colors[y * rt.width + x]; if (c.b > 180 && c.r < 80 && c.g < 80) bodyPixels++;
          if (x > rightEdge && c.r > 180 && c.g > 180 && c.b < 80) questPixels++;
          if (x > rightEdge && c.r > 180 && c.g > 180 && c.b > 180) textPixels++; }
        Check(bodyPixels > 2000 && questPixels > 200 && textPixels > 40,
            "original quest ink and text remain readable beside an actually rendered foreground character volume");
        File.WriteAllBytes(Path.Combine(output, "actual-quest-beside-character.png"), pixels.EncodeToPNG()); Object.DestroyImmediate(pixels); Object.DestroyImmediate(bodyMaterial);
        button.onClick.Invoke(); Check(clicked == 1, "tooltip placement leaves the original skin button callback unchanged");
        Check(host.sizeDelta == fixedSize && local.pivot == originalPivot, "all original fixed geometry survives the complete hover sequence");
        Call(world, "ResolveHostOwner", false, false); Check(!(bool)Get(world, "_characterHintOnRight"), "closed tooltip releases its side-lane decision");
        converted.Clear(); rt.Release(); Object.DestroyImmediate(rt);
        yield return Done;
    }

    private static void NativeRight(Camera camera, RectTransform frame, RectTransform hint, string message)
    {
        var corners = new Vector3[4]; hint.GetWorldCorners(corners); float left = float.MaxValue;
        foreach (Vector3 corner in corners)
            left = Mathf.Min(left, camera.rect.x + camera.WorldToViewportPoint(corner).x * camera.rect.width);
        float originalPixel = frame.TransformVector(Vector3.right).magnitude;
        float gap = 12f * originalPixel / (2f * camera.orthographicSize * camera.aspect) * camera.rect.width;
        Check(Mathf.Abs(left - 1f - gap) < .00003f, message);
    }

    private static IEnumerator RunNativeFlat(string output)
    {
        Directory.CreateDirectory(output);
        Type flatType = mod.GetType("GloomhavenVR.WorldUI.FlatScreen");
        Type worldType = mod.GetType("GloomhavenVR.WorldUI.WorldTooltips");
        Type policy = mod.GetType("GloomhavenVR.WorldUI.CharacterTooltipPlacement");
        object world = Activator.CreateInstance(worldType, true);
        var camera = new GameObject("original native UI camera").AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 5.4f; camera.aspect = 1988f / 1080f;
        camera.transform.position = new Vector3(0, 0, -10); camera.clearFlags = CameraClearFlags.SolidColor;
        var target = new RenderTexture(1988, 1080, 24); target.Create(); camera.targetTexture = target;
        object capturedRecord = Activator.CreateInstance(flatType.GetNestedType("CapturedCamera", BindingFlags.NonPublic), true);
        Set(capturedRecord, "Camera", camera); Set(capturedRecord, "IsUi", true);
        IDictionary captured = (IDictionary)Get(flatType, "CapturedSet"); captured.Add(camera, capturedRecord);
        var frame = Rect("original native full-UI canvas", null, new Vector2(1988, 1080), Vector2.zero);
        Canvas source = frame.gameObject.AddComponent<Canvas>(); source.renderMode = RenderMode.ScreenSpaceCamera;
        source.worldCamera = camera; source.planeDistance = 1;
        var partyRect = Rect("original character subwindow", frame, new Vector2(1300, 980), new Vector2(-200, 0));
        UIAdventurePartyAssemblyCharacterDisplay party = Native<UIAdventurePartyAssemblyCharacterDisplay>(partyRect.gameObject);
        var skinRect = Rect("arbitrary native name", partyRect, new Vector2(240, 50), new Vector2(420, -150));
        UICharacterSkinSelector selector = Native<UICharacterSkinSelector>(skinRect.gameObject);
        UITextTooltipTarget skin = Native<UITextTooltipTarget>(skinRect.gameObject); Set(selector, "skinTooltip", skin);
        var infoRoot = Rect("native campaign information", partyRect, new Vector2(600, 700), new Vector2(-200, 0));
        UICampaignAssemblyCharacterInformation info = Native<UICampaignAssemblyCharacterInformation>(infoRoot.gameObject);
        Set(party, "information", new List<UICharacterInformation> { info });
        var titleRect = Rect("original description target", partyRect, new Vector2(450, 60), new Vector2(-200, 350));
        UITextTooltipTarget title = Native<UITextTooltipTarget>(titleRect.gameObject); Set(info, "descriptionTooltip", title);
        var questRect = Rect("original quest-state print", infoRoot, new Vector2(522, 170), new Vector2(17, -90));
        Native<UIPersonalQuestState>(questRect.gameObject); Native<UIPersonalQuest>(questRect.gameObject);
        var concealRect = Rect("original conceal target", questRect, new Vector2(32, 32), new Vector2(240, 60));
        UITextTooltipTarget conceal = Native<UITextTooltipTarget>(concealRect.gameObject); Set(info, "personalQuestTooltip", conceal);
        var localRect = Rect("original local quest explanation", questRect, new Vector2(600, 130.31f), new Vector2(-15, -92));
        localRect.pivot = new Vector2(1, .5f); localRect.gameObject.AddComponent<Image>().color = Color.yellow;
        UILocalTooltip quest = Native<UILocalTooltip>(localRect.gameObject); Set(quest, "target", questRect); Set(quest, "targetOffset", new Vector2(-15, -90));
        var creatorRect = Rect("native creator window", frame, new Vector2(1300, 980), Vector2.zero);
        Native<UICharacterCreatorWindow>(creatorRect.gameObject);
        var rosterRect = Rect("native class roster", creatorRect, new Vector2(600, 700), new Vector2(-280, 0));
        UICharacterCreatorClassRoster roster = Native<UICharacterCreatorClassRoster>(rosterRect.gameObject);
        var perksRect = Rect("native starting-perks target", rosterRect, new Vector2(400, 60), new Vector2(0, -200));
        UITextTooltipTarget perks = Native<UITextTooltipTarget>(perksRect.gameObject);
        TextLocalizedListener perksInfo = Native<TextLocalizedListener>(perksRect.gameObject); Set(roster, "information", perksInfo);
        var ordinaryRect = Rect(skinRect.name, partyRect, skinRect.sizeDelta, new Vector2(200, 250));
        UITextTooltipTarget ordinary = Native<UITextTooltipTarget>(ordinaryRect.gameObject);
        var tooltipRoot = Rect("original native tooltip canvas", null, new Vector2(1988, 1080), Vector2.zero);
        Canvas tooltipCanvas = tooltipRoot.gameObject.AddComponent<Canvas>(); tooltipCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        tooltipCanvas.worldCamera = camera; tooltipCanvas.planeDistance = .5f;
        var body = Rect("original native tooltip body", tooltipRoot, new Vector2(320, 160), Vector2.zero);
        Image plate = body.gameObject.AddComponent<Image>(); plate.color = Color.yellow;
        UITooltip tooltip = Native<UITooltip>(body.gameObject); tooltip.enabled = true;
        Set(tooltip, "m_Rect", body); Set(tooltip, "m_Canvas", tooltipCanvas); Set(tooltip, "m_image", plate);
        Set(tooltip, "m_CanvasGroup", body.GetComponent<CanvasGroup>());
        Set(tooltip, "m_LayoutGroup", body.GetComponent<VerticalLayoutGroup>());
        Set(tooltip, "m_SizeFitter", body.GetComponent<ContentSizeFitter>()); Set(typeof(UITooltip), "mInstance", tooltip);
        var font = TMPro.TMP_FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("Arial.ttf"));
        Set(tooltip, "m_TitleFont", font); Set(tooltip, "m_PCTitleFontSize", 30); tooltip.transition = UITooltip.Transition.None;
        harmony.Patch(AccessTools.PropertyGetter(typeof(InputManager), "GamePadInUse"),
            prefix: new HarmonyMethod(typeof(CharacterTooltipRunner), nameof(NativeMouseInput)));
        UIManager manager = Native<UIManager>(new GameObject("original native UI manager boundary"));
        Set(manager, "uiCamera", camera); Set(typeof(UIManager), "Instance", manager);
        Set(world, "_canvas", tooltipCanvas); Set(world, "_tooltip", tooltip);
        Button button = skinRect.gameObject.AddComponent<Button>(); int clicked = 0; button.onClick.AddListener(() => clicked++);
        Transform originalParent = body.parent, originalLocalParent = localRect.parent;
        var pointers = new[] { skin, title, conceal, perks };
        try
        {
            foreach (Rect viewport in new[] { new Rect(0, 0, 1, 1), new Rect(.15f, .1f, .7f, .8f) })
            {
                camera.rect = viewport; Canvas.ForceUpdateCanvases(); Vector2 fixedFrame = frame.rect.size;
                foreach (UITextTooltipTarget pointer in pointers)
                {
                    pointer.Initialize(UITooltip.Corner.TopRight, Vector2.zero, false, 320, 160, false, false);
                    pointer.SetText("ORIGINAL NATIVE CHARACTER HINT");
                    pointer.OnPointerEnter(new PointerEventData(null)); Canvas.ForceUpdateCanvases();
                    // The original callback may complete native camera/canvas
                    // layout. Freeze the observed frame at the mod's entry.
                    fixedFrame = frame.rect.size; Vector2 fixedParty = partyRect.sizeDelta;
                    Vector2 pivot = body.pivot; Quaternion rotation = body.localRotation; float z = body.localPosition.z;
                    Call(world, "LateTick");
                    NativeRight(camera, frame, body, viewport.width < 1 ? "native partial viewport seats beside the full fixed screen frame"
                        : "native character hint clears the full fixed screen frame");
                    Check(pointer.TooltipShown && (RectTransform)Get(tooltip, "m_AnchorToTarget") == pointer.transform,
                        "native pointer callback retains the actual target and shown bookkeeping");
                    Check(body.parent == originalParent && body.pivot == pivot && body.localRotation == rotation
                        && body.localPosition.z == z && tooltipCanvas.renderMode == RenderMode.ScreenSpaceCamera,
                        "native annotation retains original parent, canvas mode, pivot, rotation and depth styling");
                    Check(frame.rect.size == fixedFrame && partyRect.sizeDelta == fixedParty,
                        "native character hints leave full screen and character subwindow geometry fixed");
                    TMPro.TextMeshProUGUI text = body.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                    Check(text != null && text.text == "ORIGINAL NATIVE CHARACTER HINT", "native character hint retains original content construction");
                    var ready = new List<Graphic>();
                    Call(mod.GetType("GloomhavenVR.WorldUI.MenuDepth.MenuDepthSources"), "CollectReadyGraphics", ready);
                    Check(ready.Contains(plate), "native annotation retains head capture without a ready character source");
                    int before = classifications;
                    for (int i = 0; i < 200; i++) Call(world, "LateTick");
                    Check(classifications == before, "warm native ticks never repeat character hierarchy classification");
                    body.sizeDelta += new Vector2(140, 80); Canvas.ForceUpdateCanvases(); Call(world, "LateTick");
                    NativeRight(camera, frame, body, "late native shared hint layout keeps the full fixed screen frame");
                    pointer.OnPointerExit(null); Call(world, "LateTick");
                    Check(!pointer.TooltipShown && tooltip.alpha == 0f && (RectTransform)Get(tooltip, "m_AnchorToTarget") == null,
                        "native pointer exit remains sole hide and content-lifetime authority");
                    ready.Clear(); Call(mod.GetType("GloomhavenVR.WorldUI.MenuDepth.MenuDepthSources"), "CollectReadyGraphics", ready);
                    Check(!ready.Contains(plate), "hidden native hint withdraws its head capture membership");
                    yield return null;
                }
                ordinary.Initialize(UITooltip.Corner.TopRight, Vector2.zero, false, 320, 160, false, false);
                ordinary.SetText("ORDINARY SAME-NAME NATIVE HINT"); ordinary.OnPointerEnter(new PointerEventData(null));
                Canvas.ForceUpdateCanvases(); Vector3 nativePosition = body.localPosition; Call(world, "LateTick");
                Check(Vector3.Distance(nativePosition, body.localPosition) < 1e-6f, "native ordinary hint keeps the original position");
                var ordinaryReady = new List<Graphic>();
                Call(mod.GetType("GloomhavenVR.WorldUI.MenuDepth.MenuDepthSources"), "CollectReadyGraphics", ordinaryReady);
                Check(!ordinaryReady.Contains(plate), "ordinary same-name hint gains no annotation head capture membership");
                ordinary.OnPointerExit(null); Call(world, "LateTick"); yield return null;
                localRect.sizeDelta = new Vector2(600, 130.31f); quest.Show(); Canvas.ForceUpdateCanvases();
                NativeRight(camera, frame, localRect, "native local quest clears the full fixed screen frame");
                var localReady = new List<Graphic>();
                Call(mod.GetType("GloomhavenVR.WorldUI.MenuDepth.MenuDepthSources"), "CollectReadyGraphics", localReady);
                Check(localReady.Contains(localRect.GetComponent<Image>()), "native local quest retains head capture without a ready character source");
                Check(localRect.parent == originalLocalParent && localRect.pivot == new Vector2(1, .5f),
                    "native local quest retains its original parent and pivot");
                localRect.sizeDelta = new Vector2(720, 350); Canvas.ForceUpdateCanvases(); Call(policy, "TickNativeLocals", true);
                NativeRight(camera, frame, localRect, "late native quest layout retains the full fixed screen frame");
                Check(frame.rect.size == fixedFrame, "late native quest layout cannot resize the screen frame");
                quest.Hide(); Call(policy, "TickNativeLocals", true);
                Check(!quest.IsShown && localRect.parent == originalLocalParent, "native local quest hide retains original lifetime and hierarchy");
            }
            // Teardown hands the exact native body placement back, while a new
            // original writer or reparent would win without being overwritten.
            skin.OnPointerEnter(new PointerEventData(null)); Canvas.ForceUpdateCanvases(); Vector3 native = body.localPosition;
            Call(world, "LateTick"); Call(world, "Shutdown");
            Check(Vector3.Distance(native, body.localPosition) < 1e-6f && body.parent == originalParent,
                "native tooltip shutdown restores only its own original XY write");
            button.onClick.Invoke(); Check(clicked == 1, "native flat hint leaves the original skin button callback unchanged");
            File.WriteAllText(Path.Combine(output, "native-flat-contract.txt"),
                "Original UITooltipTarget pointer enter/exit and UITextTooltipTarget content construction executed.\n"
                + "WorldTooltips.LateTick Menu2D path and native UILocalTooltip Show/Hide/RefreshPosition executed.\n"
                + "Full and partial original camera viewports covered; geometry only, no head-capture pixels claimed.\n");
        }
        finally
        {
            Call(world, "Shutdown"); Call(policy, "TickNativeLocals", false); captured.Clear();
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(font);
        }
        yield return Done;
    }
}
