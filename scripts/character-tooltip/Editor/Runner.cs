using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class CharacterTooltipRunner
{
    [Serializable] public sealed class Case { public string name, dll, expected; }
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
                routine = Run(Path.Combine(manifest.evidence, item.name));
            }
            if (routine.MoveNext()) return;
            if (!string.IsNullOrEmpty(item.expected)) throw new Exception("Negative control escaped: " + item.name);
            writer.WriteLine("PASS " + item.name + ": " + assertions + " native layout/GPU assertions");
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
}
