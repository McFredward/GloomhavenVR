// Exercise the shipped supersampler assembly, including its real allocator,
// ConvertedPanel state, layer ownership, camera callbacks and final head pixels.
// Native controller/network state remains the existing Results fixture boundary.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class ProgressionTests
{
    private const BindingFlags HeadFlags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Assembly captureAssembly = null!;
    private static Type captureType = null!, capturePanelType = null!, captureConversionType = null!;
    private static readonly Dictionary<Subject, object> capturePanels = new();
    private static ResolveEventHandler? captureResolver;
    private static ScenarioResultsPresentation? headReceiver;
    private static Subject? headObserverSubject;
    private static IEnumerator? headSteps;
    private const bool HeadBreakResolve = false;
    private static bool headStopResolve;
    private static int headRejectedResolves;
    private static object? headHarmony;
    private static Type? headHarmonyType;
    private static bool HeadResolvePrefix() { if (headStopResolve) { headRejectedResolves++; return false; } return true; }
    private static void HeadInstallResolveControl()
    {
        headHarmonyType = Type.GetType("HarmonyLib.Harmony, 0Harmony", true)!;
        headHarmony = Activator.CreateInstance(headHarmonyType, new object[] { "results-head-" + typeof(ProgressionTests).Assembly.GetName().Name });
        Type methodType = Type.GetType("HarmonyLib.HarmonyMethod, 0Harmony", true)!;
        object prefix = Activator.CreateInstance(methodType, new object[] { typeof(ProgressionTests).GetMethod("HeadResolvePrefix", HeadFlags)! })!;
        MethodInfo patch = headHarmonyType.GetMethods().Single(m => m.Name == "Patch" && m.GetParameters().Length == 5);
        patch.Invoke(headHarmony, new object?[] { captureType.GetMethod("ResolveAndMip", HeadFlags), prefix, null, null, null });
    }
    public static bool HeadAdvance() => headSteps!.MoveNext();
    public static int HeadAssertions() => count;
    public static void HeadFinish() { (headSteps as IDisposable)?.Dispose(); headSteps = null; }
    private static object? CaptureCall(Type type, string name, params object[] args)
        => type.GetMethod(name, HeadFlags)!.Invoke(null, args);
    private static void CaptureField(object target, string name, object? value)
        => target.GetType().GetField(name, HeadFlags)!.SetValue(target, value);
    private static void CaptureInit(Camera head)
    {
        string runtime = Path.Combine(inputs, "capture-runtime");
        string[] roots = File.ReadAllLines(Path.Combine(runtime, "assembly-roots.txt"));
        captureResolver = (_, request) => {
            string file = new AssemblyName(request.Name).Name + ".dll";
            foreach (string root in roots) {
                string path = Path.Combine(root, file);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += captureResolver;
        captureAssembly = Assembly.LoadFrom(Path.Combine(runtime, "GloomhavenVR.dll"));
        captureType = captureAssembly.GetType("GloomhavenVR.WorldUI.PanelSupersample", true)!;
        capturePanelType = captureAssembly.GetType("GloomhavenVR.WorldUI.ConvertedPanel", true)!;
        captureConversionType = captureAssembly.GetType("GloomhavenVR.WorldUI.CanvasConversion", true)!;
        Type rig = captureAssembly.GetType("GloomhavenVR.Rig.VRRigDriver", true)!;
        rig.GetField("<HeadCamera>k__BackingField", HeadFlags)!.SetValue(null, head);
        captureAssembly.GetType("GloomhavenVR.Core.VRSession", true)!.GetProperty("IsRunning", HeadFlags)!.SetValue(null, true);
        Type.GetType("BepInEx.Paths, BepInEx", true)!.GetProperty("BepInExConfigPath", HeadFlags)!.SetValue(null, Path.Combine(runtime, "BepInEx.cfg"));
        Type configFile = Type.GetType("BepInEx.Configuration.ConfigFile, BepInEx", true)!;
        object config = Activator.CreateInstance(configFile, new object?[] { Path.Combine(runtime, "fixture.cfg"), false, null })!;
        MethodInfo bind = configFile.GetMethods().Single(m => m.Name == "Bind" && m.IsGenericMethodDefinition && m.GetParameters().Length == 4 && m.GetParameters()[0].ParameterType == typeof(string) && m.GetParameters()[3].ParameterType == typeof(string));
        object enabled = bind.MakeGenericMethod(typeof(bool)).Invoke(config, new object[] { "WorldUI", "PanelSupersample", true, "fixture input" })!;
        captureAssembly.GetType("GloomhavenVR.WorldUI.WorldUIConfig", true)!.GetField("PanelSupersample", HeadFlags)!.SetValue(null, enabled);
        object scale = bind.MakeGenericMethod(typeof(float)).Invoke(config, new object[] { "WorldUI", "CanvasScaleMm", 1f, "fixture input" })!;
        captureAssembly.GetType("GloomhavenVR.WorldUI.WorldUIConfig", true)!.GetField("CanvasScaleMm", HeadFlags)!.SetValue(null, scale);
        Check(captureType.Assembly.GetName().Name == "GloomhavenVR", "head proof uses the complete shipped capture assembly without copied coordinator kernels");
        File.WriteAllText(Path.Combine(evidence, "capture-assembly.txt"), captureAssembly.Location + "\n" + captureAssembly.ManifestModule.ModuleVersionId + "\n");
        HeadInstallResolveControl();
    }
    private static void HeadNativeLayout(RectTransform root, XmlElement node)
    {
        foreach (XmlElement component in node.SelectNodes("component")) {
            string kind = component.GetAttribute("kind");
            if (kind != "VerticalLayoutGroup" && kind != "HorizontalLayoutGroup" && kind != "ContentSizeFitter" && kind != "LayoutElement") continue;
            Type type = typeof(LayoutGroup).Assembly.GetType("UnityEngine.UI." + kind, true)!;
            Component actual = root.GetComponent(type) ?? root.gameObject.AddComponent(type);
            JsonUtility.FromJsonOverwrite(component.GetAttribute("json"), actual);
        }
        int index = 0;
        foreach (XmlElement child in node.SelectNodes("node")) HeadNativeLayout((RectTransform)root.GetChild(index++), child);
    }
    private static void HeadNativeLayout(Subject subject)
    {
        var xml = new XmlDocument(); xml.Load(Path.Combine(inputs, "results.xml"));
        HeadNativeLayout(subject.Root, xml.DocumentElement!);
        foreach (Transform row in subject.Manager.resultsPanel.transform) {
            string file = row.name.StartsWith("OriginalBattleGoal", StringComparison.Ordinal) ? "battle.xml" : row.name.StartsWith("OriginalAchievement", StringComparison.Ordinal) ? "achievement.xml" : "";
            if (file.Length == 0) continue;
            xml.Load(Path.Combine(inputs, file)); HeadNativeLayout((RectTransform)row, xml.DocumentElement!);
        }
        // Original row instances make a genuinely scrollable native table. No
        // substitute rectangles replace its text, cell materials or layout.
        Transform list = subject.Root.GetComponentsInChildren<VerticalLayoutGroup>(true).First(x => x.name == "UI Scenario Accomplishments").transform;
        for (int row = 8; row < 20; row++) {
            GameObject copy = Object.Instantiate(list.GetChild(0).gameObject, list, false);
            copy.name = "UI Scenario Accomplishment Row (" + row + ")";
        }
        foreach (var text in subject.Manager.header.GetComponentsInChildren<TMPro.TMP_Text>(true)) text.text = "VICTORY";
        ScrollRect scroll = subject.Root.GetComponentInChildren<ScrollRect>(true);
        scroll.viewport = (RectTransform)scroll.transform.Find("Viewport");
        scroll.content = (RectTransform)scroll.viewport.Find("Content");
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
    }
    private static void CaptureAdd(Subject subject)
    {
        RectTransform nativeHome = subject.Canvas;
        MethodInfo convert = captureConversionType.GetMethod("Convert", HeadFlags)!;
        object?[] arguments = convert.GetParameters().Select(p => p.DefaultValue).ToArray();
        arguments[0] = subject.Root; arguments[1] = "ActualResultsHead";
        arguments[2] = false; arguments[4] = false; arguments[8] = true;
        arguments[9] = true;
        object panel = convert.Invoke(null, arguments)!;
        Check(panel != null, "complete production conversion allocates the native world-space host");
        subject.Host = (RectTransform)capturePanelType.GetField("HostRect", HeadFlags)!.GetValue(panel)!;
        subject.Canvas = subject.Host;
        subject.Panel.HostRect = subject.Host; subject.Panel.HostGo = subject.Host.gameObject;
        subject.Panel.HostCanvas = (Canvas)capturePanelType.GetField("HostCanvas", HeadFlags)!.GetValue(panel)!;
        subject.Ready.transform.SetParent(subject.Host, false);
        nativeHome.gameObject.SetActive(false);
        capturePanels.Add(subject, panel!);
    }
    private static byte[] HeadImage(Camera head, Subject subject, string name)
    {
        foreach (var pair in capturePanels) {
            CaptureField(pair.Value, "FitContentRoot", pair.Key.Panel.FitContentRoot);
            CaptureField(pair.Value, "FitEnabled", pair.Key.Panel.FitEnabled);
            // Actual conversion/reveal retains exclusive render visibility ownership.
        }
        foreach (var pair in capturePanels) {
            object panel = pair.Value;
            if ((bool)capturePanelType.GetField("RevealPending", HeadFlags)!.GetValue(panel)!) {
                CaptureField(panel, "RevealDeadline", -1f); // explicit native clock boundary
                CaptureCall(captureConversionType, "TickRevealGate", panel);
                CaptureCall(captureConversionType, "CompleteReveal", panel);
            }
        }
        if (headReceiver != null && ReferenceEquals(subject, headObserverSubject)) headReceiver.Tick();
        Canvas.ForceUpdateCanvases();
        CaptureCall(captureType, "Tick");
        CaptureCall(captureType, "LateTick");
        var entries = (IList)captureType.GetField("Entries", HeadFlags)!.GetValue(null)!;
        object entry = entries.Cast<object>().Single(e => ReferenceEquals(e.GetType().GetField("Panel", HeadFlags)!.GetValue(e), capturePanels[subject]));
        Camera camera = (Camera)entry.GetType().GetField("Cam", HeadFlags)!.GetValue(entry)!;
        Check(camera != null && camera.targetTexture != null && camera.targetTexture.IsCreated(), "actual capture coordinator admits and allocates the live native window");
        File.WriteAllLines(Path.Combine(evidence, name + ".graphics.txt"), subject.Host.GetComponentsInChildren<Graphic>(true).Select(g => g.name + " active=" + g.gameObject.activeInHierarchy + " enabled=" + g.enabled + " layer=" + g.gameObject.layer + " canvas=" + g.canvas + " cull=" + g.canvasRenderer.cull + " alpha=" + g.canvasRenderer.GetColor().a + "/" + g.canvasRenderer.GetInheritedAlpha() + " rect=" + Bounds(subject.Host, g.rectTransform)));
        File.AppendAllText(Path.Combine(evidence, "frame-counts.txt"), name + " Unity=" + Time.frameCount + " nextSweep=" + entry.GetType().GetField("NextSweepFrame", HeadFlags)!.GetValue(entry) + " cameraMask=" + camera!.cullingMask + "\n");
        camera!.Render(); // enabled capture camera precedes the actual head camera
        byte[] pixels = Image(head, name);
        var display = (RawImage)entry.GetType().GetField("DisplayImage", HeadFlags)!.GetValue(entry)!;
        Check(display.texture != null && !ReferenceEquals(display.texture, camera.targetTexture), "final head samples the actual resolved display texture rather than native direct rendering");
        File.AppendAllText(Path.Combine(evidence, "capture-frames.txt"), name + " host=" + subject.Host.rect + " capture=" + entry.GetType().GetField("Frame", HeadFlags)!.GetValue(entry) + " target=" + camera.targetTexture!.width + "x" + camera.targetTexture!.height + "\n");
        return pixels;
    }
    private static void HeadSame(byte[] expected, byte[] observed, string message)
    {
        int changed = 0;
        for (int i = 0; i < expected.Length; i++) if (Math.Abs(expected[i] - observed[i]) > 4) changed++;
        File.AppendAllText(Path.Combine(evidence, "head-differences.txt"), message + ": " + changed + "/" + expected.Length + "\n");
        Check(changed < expected.Length * .003, message);
    }
    private static int HeadRun() { headSteps = HeadSteps(); return 0; }
    private static IEnumerator HeadSteps()
    {
        footerProof = true;
        var head = new GameObject("FinalHead", typeof(Camera)).GetComponent<Camera>();
        head.tag = "MainCamera"; head.transform.position = new Vector3(0, 0, -2);
        head.orthographic = true; head.orthographicSize = .7f; head.aspect = 16f / 9;
        head.clearFlags = CameraClearFlags.SolidColor; head.backgroundColor = Color.black;
        CaptureInit(head);
        var sender = new ScenarioResultsPresentation(); var receiver = new ScenarioResultsPresentation(); headReceiver = receiver;
        var author = Make(head, "HeadAuthor"); var observer = Make(head, "HeadObserver"); headObserverSubject = observer;
        HeadNativeLayout(author); HeadNativeLayout(observer);
        // Nonplaying engine sentinels make accidental controller/audio copying
        // falsifiable without replaying the game's global audio callbacks.
        AudioSource authorAudio = author.Root.gameObject.AddComponent<AudioSource>(); authorAudio.playOnAwake = false;
        Animator authorAnimator = author.Manager.header.gameObject.AddComponent<Animator>();
        AudioSource observerAudio = observer.Root.gameObject.AddComponent<AudioSource>(); observerAudio.playOnAwake = false;
        Animator observerAnimator = observer.Manager.header.gameObject.AddComponent<Animator>();
        CaptureAdd(author); CaptureAdd(observer); observer.Canvas.gameObject.SetActive(false);
        try {
            Select(author); RemoveRows(author); author.Manager.statsPanel.SetActive(false); author.Manager.buttonsContainer.SetActive(false); author.Ready.gameObject.SetActive(false);
            for (int frame = 0; frame < 20; frame++) yield return null;
            byte[] headingAuthor = HeadImage(head, author, "head-heading-author.png");
            var heading = State(291, 1, ResultsClock.Now); sender.Capture(heading);
            author.Canvas.gameObject.SetActive(false); observer.Canvas.gameObject.SetActive(true); Select(observer); RemoveRows(observer); observer.Ready.gameObject.SetActive(false);
            receiver.Apply(heading); receiver.Tick();
            yield return null;
            HeadSame(headingAuthor, HeadImage(head, observer, "head-heading-observer.png"), "real coordinator and final head pixels retain the cold original Victory stage");
            CanvasGroup headerAlpha = author.Manager.header.GetComponent<CanvasGroup>();
            RectTransform headerRect = (RectTransform)author.Manager.header.transform;
            Vector2 finalHeader = headerRect.anchoredPosition;
            for (int step = 0; step < 3; step++) {
                observer.Canvas.gameObject.SetActive(false); author.Canvas.gameObject.SetActive(true); Select(author);
                headerAlpha.alpha = new[] { .25f, .65f, 1f }[step];
                headerRect.anchoredPosition = finalHeader + new Vector2(0, -40f + step * 20f);
                for (int frame = 0; frame < 5; frame++) yield return null;
                byte[] nativeAnimation = HeadImage(head, author, "head-reveal-" + step + "-author.png");
                ResultsClock.Now += .3f; ResultsClock.Frame++;
                var animated = State(291, (ulong)(step + 2), ResultsClock.Now); sender.Capture(animated);
                byte[] packet = ScenarioResultsCodec.Write(animated);
                Check(ScenarioResultsCodec.TryRead(packet, packet.Length, out var remoteAnimation) && remoteAnimation != null, "intermediate native header output crosses the production codec");
                author.Canvas.gameObject.SetActive(false); observer.Canvas.gameObject.SetActive(true); Select(observer);
                receiver.Apply(remoteAnimation!); ResultsClock.Frame++; receiver.Tick(); ResultsClock.Now += .3f; ResultsClock.Frame++; receiver.Tick();
                for (int frame = 0; frame < 5; frame++) yield return null;
                HeadSame(nativeAnimation, HeadImage(head, observer, "head-reveal-" + step + "-observer.png"), "real final head pixels retain native header alpha and position at reveal step " + step);
                Transform picture = observer.Panel.ExtraRenderRoots.Single();
                Check(picture.GetComponentsInChildren<AudioSource>(true).Length == 0 && picture.GetComponentsInChildren<Animator>(true).Length == 0, "inert observer pictures cannot replay original animation or audio callbacks");
                Check(authorAudio != null && authorAudio.enabled && authorAnimator != null && authorAnimator.enabled && observerAudio != null && observerAudio.enabled && observerAnimator != null && observerAnimator.enabled, "capture and public playback retain source audio and animator identity and enabled state");
            }
            byte[]? preceding = null;
            for (int stage = 0; stage < 3; stage++) {
                observer.Canvas.gameObject.SetActive(false); author.Canvas.gameObject.SetActive(true); Select(author);
                author.Manager.statsPanel.SetActive(true); author.Manager.buttonsContainer.SetActive(true); author.Ready.gameObject.SetActive(true);
                var scroll = author.Root.GetComponentInChildren<ScrollRect>(true);
                scroll.verticalNormalizedPosition = stage * .5f; Canvas.ForceUpdateCanvases();
                for (int frame = 0; frame < 20; frame++) yield return null;
                ResultsClock.Now += .3f; ResultsClock.Frame++;
                var full = State(291, (ulong)(stage + 10), ResultsClock.Now); sender.Capture(full);
                byte[] expected = HeadImage(head, author, "head-stats-" + stage + "-author.png");
                Check(!expected.SequenceEqual(headingAuthor), "actual head pixels change from Victory to the native scroll table");
                if (preceding != null) Check(!expected.SequenceEqual(preceding), "actual author head pixels change with native scroll travel");
                preceding = expected;
                byte[] packet = ScenarioResultsCodec.Write(full); File.WriteAllBytes(Path.Combine(evidence, "head-stage-" + stage + ".bin"), packet);
                Check(ScenarioResultsCodec.TryRead(packet, packet.Length, out var decoded) && decoded != null, "complete native table crosses the unchanged production codec before head rendering");
                author.Canvas.gameObject.SetActive(false); observer.Canvas.gameObject.SetActive(true); Select(observer); headStopResolve = HeadBreakResolve; receiver.Apply(decoded!); ResultsClock.Frame++; receiver.Tick(); ResultsClock.Now += .3f; ResultsClock.Frame++; receiver.Tick();
                for (int frame = 0; frame < 20; frame++) yield return null;
                HeadSame(expected, HeadImage(head, observer, "head-stats-" + stage + "-observer.png"), "real coordinator and final head pixels retain complete original table and scroll stage " + stage);
            }
            yield break;
        }
        finally {
            File.WriteAllText(Path.Combine(evidence, "actual-resolve-control.txt"), "suppressed_actual_resolves=" + headRejectedResolves + "\n");
            headStopResolve = false;
            if (headHarmony != null) {
                MethodInfo unpatch = headHarmonyType!.GetMethods().Single(m => m.Name == "UnpatchAll" && m.GetParameters().Length == 1);
                unpatch.Invoke(unpatch.IsStatic ? null : headHarmony, new object[] { "results-head-" + typeof(ProgressionTests).Assembly.GetName().Name });
            }
            CaptureCall(captureType, "Shutdown");
            ((IList)captureConversionType.GetField("Active", HeadFlags)!.GetValue(null)!).Clear();
            capturePanels.Clear(); headReceiver = null; headObserverSubject = null; sender.Reset(); receiver.Reset();
            if (captureResolver != null) AppDomain.CurrentDomain.AssemblyResolve -= captureResolver;
        }
    }
}
