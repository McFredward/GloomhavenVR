using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;

public static class InteractionProgram
{
    public static int Checks;
    public static string Metrics = "";
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;
    private static void Check(bool ok, string message) { Checks++; if (!ok) throw new Exception(message); }
    private static object? Field(string name) => typeof(CanvasConversion).GetField(name, Private)!.GetValue(null);
    private static void Release(ConvertedPanel panel) => typeof(CanvasConversion).GetMethod("ReleaseHiddenWindowVeil", Private)!.Invoke(null, new object[] { panel });
    private static object State(ConvertedPanel panel) => ((IDictionary)Field("HiddenWindowVeils")!)[panel]!;
    private static IList Windows(ConvertedPanel panel) => (IList)State(panel).GetType().GetField("Windows")!.GetValue(State(panel))!;
    private static int Holds(CanvasRenderer renderer)
    {
        IDictionary holds = (IDictionary)Field("VeilHolds")!;
        object? value = holds[renderer];
        return value == null ? 0 : (int)value.GetType().GetField("Holds")!.GetValue(value)!;
    }
    private static GameObject Node(string name, Transform? parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }
    private static UIWindow Window(GameObject go)
    {
        UIWindow window = go.AddComponent<UIWindow>();
        window.escapeKeyAction = UIWindow.EscapeKeyAction.None;
        window.backKeyAction = UIWindow.BackKeyAction.None;
        go.GetComponent<CanvasGroup>().alpha = 0f;
        return window;
    }
    private static Image Graphic(string name, Transform parent)
    {
        Image image = Node(name, parent).AddComponent<Image>();
        image.onCullStateChanged.AddListener(_ => Proof.NativeCullCallbacks++);
        return image;
    }
    private static void MutateAfterFirstPanel(List<ConvertedPanel> panels, Action action)
    {
        ConvertedPanel first = panels[0];
        first.RevealPending = first.RevealArmed = true;
        CanvasConversion.RevealCallback = panel => { if (ReferenceEquals(panel, first)) action(); };
        try { CanvasConversion.LateTick(); }
        finally { CanvasConversion.RevealCallback = null; }
    }
    public static IEnumerator Run()
    {
        Check(typeof(UIWindow).Assembly.GetName().Name == "GH.Runtime", "registry fixture executes the publisher's original UIWindow");
        var panels = new List<ConvertedPanel>();
        var windows = new List<UIWindow>();
        var graphics = new List<Image>();
        for (int p = 0; p < 20; p++)
        {
            GameObject host = Node("panel " + p);
            host.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            GameObject target = Node("native target", host.transform);
            var panel = new ConvertedPanel { HostGo = host, Target = target.transform };
            panels.Add(panel); CanvasConversion.Active.Add(panel);
            UIWindow window = Window(Node("hidden native window", target.transform)); windows.Add(window);
            Transform parent = window.transform;
            for (int level = 0; level < 5; level++)
            {
                GameObject levelNode = Node("group " + level, parent);
                levelNode.AddComponent<CanvasGroup>().alpha = .95f;
                parent = levelNode.transform;
            }
            for (int g = 0; g < 40; g++) graphics.Add(Graphic("original graphic " + g, parent));
        }
        for (int f = 0; f < 80; f++) Window(Node("foreign registered window " + f));
        // Let genuine native Start and uGUI service the original controls, then exercise our pass.
        yield return null;
        Canvas.ForceUpdateCanvases();
        Check(UIWindow.GetWindows().Count == 100, "real native OnEnable registers all 100 windows");

        PerfConfig.SharedUiWindowReadsOn = false;
        Proof.Reset(); CanvasConversion.LateTick();
        int independentMembers = Proof.RegistryMembers, independentGroups = Proof.GroupReads;
        Check(Proof.NativeCullCallbacks == 0, "direct native renderer ownership writes invoke no managed cull callback inside collection");
        Check(Proof.RegistryScans == 20 && independentMembers == 2000, "off path independently reads 100 native windows for each of 20 panels");
        Check(PerfMonitor.Value("UI.WindowRegistryIndependentScans") == Proof.RegistryScans && PerfMonitor.Value("UI.WindowRegistryIndependentMembers") == Proof.RegistryMembers, "debug Off registry counters equal actual native iteration work");
        Check(PerfMonitor.Value("UI.WindowRegistrySharedScans") == 0, "debug Off does not claim shared registry work");
        foreach (Image graphic in graphics) Check(graphic.canvasRenderer.cull && graphic.canvasRenderer.GetAlpha() == 0f, "off path catches hidden content before rendering");
        foreach (ConvertedPanel panel in panels) Release(panel);
        PerfConfig.SharedUiWindowReadsOn = true;
        Proof.Reset(); CanvasConversion.LateTick();
        bool supported = Field("NativeWindowRegistryVersion") != null;
        int sharedMembers = Proof.RegistryMembers, sharedGroups = Proof.GroupReads;
        Check(Proof.NativeCullCallbacks == 0, "shared group observation spans no managed cull callback");
        Check(Proof.RegistryScans == (supported ? 1 : 20), "one exact shared registry distribution replaces 20 independent scans");
        Check(sharedMembers == (supported ? 100 : 2000), "shared pass reads each registry member once");
        Check(PerfMonitor.Value("UI.WindowRegistrySharedScans") + PerfMonitor.Value("UI.WindowRegistryIndependentScans") == Proof.RegistryScans, "debug registry scan counts equal actual shared and fallback work");
        Check(PerfMonitor.Value("UI.WindowRegistrySharedMembers") + PerfMonitor.Value("UI.WindowRegistryIndependentMembers") == Proof.RegistryMembers, "debug registry member counts equal actual shared and fallback work");
        Check(PerfMonitor.Value("UI.WindowRegistryReusePanels") == (supported ? 19 : 0), "debug reuse counts only panels reusing an existing native distribution");
        Check(sharedGroups < independentGroups / 2, "shared pure group reads remove repeated native ancestor component reads");
        foreach (Image graphic in graphics) Check(graphic.canvasRenderer.cull && graphic.canvasRenderer.GetAlpha() == 0f, "shared path catches the same hidden content before rendering");

        object rootStorage = Field("SharedVeilRoots")!, routeStorage = Field("SharedVeilRootHeads")!;
        Proof.Reset();
        for (int repeat = 0; repeat < 40; repeat++) CanvasConversion.LateTick();
        Check(Proof.RegistryScans == (supported ? 40 : 800), "steady shared routing uses one registry scan per pass");
        Check(Proof.ComponentCaptures == 0, "steady unchanged topology allocates no replacement component inventory");
        Check(PerfMonitor.Value("UI.WindowRegistrySharedScans") + PerfMonitor.Value("UI.WindowRegistryIndependentScans") == Proof.RegistryScans, "debug scan counters accumulate exact steady work across scoped passes");
        Check(PerfMonitor.Value("UI.WindowRegistrySharedMembers") + PerfMonitor.Value("UI.WindowRegistryIndependentMembers") == Proof.RegistryMembers, "debug member counters accumulate exact steady work across scoped passes");
        PerfMonitor.StepsActive = false; Proof.Reset(); CanvasConversion.LateTick();
        Check(PerfMonitor.Counts.Count == 0 && Proof.RegistryScans > 0, "normal UI rendering performs native work without diagnostic reports");
        PerfMonitor.StepsActive = true;
        VRLog.WantsDebug = false; Proof.Reset(); CanvasConversion.LateTick();
        Check(PerfMonitor.Counts.Count == 0 && Proof.RegistryScans > 0, "normal log level performs registry work without diagnostic sampling");
        VRLog.WantsDebug = true;
        Check(ReferenceEquals(rootStorage, Field("SharedVeilRoots")) && ReferenceEquals(routeStorage, Field("SharedVeilRootHeads")), "warmed routing storage is reused");
        Check(((ICollection)Field("SharedVeilRoots")!).Count == 0 && ((IDictionary)Field("SharedVeilRootHeads")!).Count == 0 && Field("SharedVeilRegistry") == null, "shared pass releases all route and registry references");

        Image? nestedArrival = null;
        MutateAfterFirstPanel(panels, () => nestedArrival = Graphic("new pooled original under an existing veil", windows[9].transform));
        Check(nestedArrival!.canvasRenderer.cull && Holds(nestedArrival.canvasRenderer) == 1, "new graphic under an existing hidden window is veiled in the same frame");
        PerfConfig.SharedUiWindowReadsOn = false;
        Image? independentArrival = null;
        MutateAfterFirstPanel(panels, () => independentArrival = Graphic("new original with sharing off", windows[9].transform));
        Check(independentArrival!.canvasRenderer.cull && Holds(independentArrival.canvasRenderer) == 1, "existing-window graphic arrival retains one-frame safety with sharing off");
        PerfConfig.SharedUiWindowReadsOn = true;

        // Adding UIWindow to an ALREADY inventoried transform produces no hierarchy event.
        GameObject arrival = Node("late component slot", panels[10].Target);
        Image arrivingGraphic = Graphic("arriving original", arrival.transform);
        CanvasConversion.LateTick();
        UIWindow? addedWindow = null;
        MutateAfterFirstPanel(panels, () => addedWindow = Window(arrival));
        Check(Windows(panels[10]).Contains(addedWindow!) && arrivingGraphic.canvasRenderer.cull, "same-frame registry growth reaches later panel before its render");

        // Disable one existing component and add another without changing registry Count.
        GameObject replacement = Node("replacement component slot", panels[11].Target);
        Image replacementGraphic = Graphic("replacement original", replacement.transform);
        CanvasConversion.LateTick();
        int before = UIWindow.GetWindows().Count;
        UIWindow? replacedWindow = null;
        MutateAfterFirstPanel(panels, () => { windows[11].enabled = false; replacedWindow = Window(replacement); });
        Check(UIWindow.GetWindows().Count == before, "replacement fixture preserves native registry count");
        Check(Windows(panels[11]).Contains(replacedWindow!) && replacementGraphic.canvasRenderer.cull, "same-count native replacement invalidates the exact shared read");
        Check(Windows(panels[11]).Contains(windows[11]), "disabled original windows remain eligible after registry replacement");

        // Reparenting changes no registry member or version. Unity events must invalidate routing.
        before = UIWindow.GetWindows().Count;
        Image movedGraphic = graphics[5 * 40];
        MutateAfterFirstPanel(panels, () => windows[5].transform.SetParent(panels[12].Target, false));
        Check(UIWindow.GetWindows().Count == before, "reparent fixture preserves registry membership");
        Check(!Windows(panels[5]).Contains(windows[5]) && Windows(panels[12]).Contains(windows[5]), "same-frame native reparenting reroutes original window identities");
        Check(movedGraphic.canvasRenderer.cull && Holds(movedGraphic.canvasRenderer) == 1, "reparented window releases its former panel hold before taking the current one");

        if (supported)
        {
            GameObject disabledArrival = Node("already inventoried disabled slot", panels[13].Target);
            Image disabledGraphic = Graphic("disabled original", disabledArrival.transform);
            CanvasConversion.LateTick();
            UIWindow? lateDisabled = null;
            MutateAfterFirstPanel(panels, () => { lateDisabled = Window(disabledArrival); lateDisabled.enabled = false; });
            Check(Windows(panels[13]).Contains(lateDisabled!) && disabledGraphic.canvasRenderer.cull, "registry edit recaptures newly disabled native component on an existing transform");
        }

        GameObject dormant = Node("inactive nested slot", panels[14].Target); dormant.SetActive(false);
        CanvasConversion.LateTick();
        UIWindow? activatedWindow = null; Image? activatedGraphic = null;
        MutateAfterFirstPanel(panels, () => { GameObject late = Node("late nested original", dormant.transform); activatedWindow = Window(late); activatedGraphic = Graphic("late nested graphic", late.transform); dormant.SetActive(true); });
        Check(Windows(panels[14]).Contains(activatedWindow!) && activatedGraphic!.canvasRenderer.cull, "inactive nested insert and activation remain same-frame");

        Image? newlyOpened = null;
        MutateAfterFirstPanel(panels, () =>
        {
            GameObject host = Node("same-frame new native panel");
            host.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            GameObject target = Node("new target", host.transform);
            var addedPanel = new ConvertedPanel { HostGo = host, Target = target.transform };
            UIWindow window = Window(Node("new nested window", target.transform));
            newlyOpened = Graphic("new opening original", window.transform);
            panels.Add(addedPanel); CanvasConversion.Active.Add(addedPanel);
        });
        Check(newlyOpened!.canvasRenderer.cull && Holds(newlyOpened.canvasRenderer) == 1, "panel added by an earlier native callback gets a complete same-frame inventory");

        // Foreign alpha/cull writes must be observed at EVERY intermediate materialise frame.
        UIWindow animated = windows[15]; Image animatedGraphic = graphics[15 * 40];
        CanvasRenderer cr = animatedGraphic.canvasRenderer;
        Check(CanvasConversion.PreVeilAlpha(cr, cr.GetAlpha()) == 1f, "materialise captures original alpha through the real veil");
        panels[15].Animating = true;
        for (int step = 1; step <= 8; step++)
        {
            float alpha = step / 10f;
            cr.SetAlpha(alpha); cr.cull = false;
            CanvasConversion.LateTick();
            Check(cr.cull && cr.GetAlpha() == 0f && Mathf.Approximately(CanvasConversion.PreVeilAlpha(cr, 0f), alpha), "every intermediate foreign materialise alpha is learned and reasserted");
            yield return null;
        }
        animated.GetComponent<CanvasGroup>().enabled = false;
        CanvasConversion.LateTick();
        Check(cr.cull, "materialise hold never unmasks a hidden native sibling");
        animated.GetComponent<CanvasGroup>().enabled = true;
        animated.GetComponent<CanvasGroup>().alpha = .01f;
        CanvasConversion.LateTick();
        Check(!cr.cull && Mathf.Approximately(cr.GetAlpha(), .8f), "first nonzero native show tween lifts immediately with exact latest alpha");
        panels[15].Animating = false;

        // Live transforms, original content/material and all show-tween values are never cached.
        Image visible = animatedGraphic;
        Texture2D texture = new(16, 16); Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(.5f, .5f));
        Material material = new(Shader.Find("UI/Default")); visible.sprite = sprite; visible.material = material;
        for (int step = 1; step <= 8; step++)
        {
            Vector3 position = new(step * 3f, -step * 2f, step);
            visible.rectTransform.localPosition = position; visible.rectTransform.sizeDelta = new Vector2(25 + step, 11 + step);
            animated.GetComponent<CanvasGroup>().alpha = step / 8f;
            CanvasConversion.LateTick();
            Check(!cr.cull && visible.sprite == sprite && visible.material == material && visible.rectTransform.localPosition == position && visible.rectTransform.rect.width == 25 + step, "same-frame original content, material and geometry survive every native animation step");
        }

        // Pooled native graphics can leave a dormant window for a visible hand in the same frame.
        Image pooled = graphics[16 * 40]; pooled.canvasRenderer.SetAlpha(.37f); CanvasConversion.LateTick();
        panels[16].Dormant = true;
        GameObject hand = Node("visible physical hand"); hand.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        pooled.transform.SetParent(hand.transform, false);
        CanvasConversion.LateTick();
        Check(!pooled.canvasRenderer.cull && Mathf.Approximately(pooled.canvasRenderer.GetAlpha(), .37f) && Holds(pooled.canvasRenderer) == 0, "dormant panel releases pooled renderer with exact foreign alpha in the same frame");

        // Ignore-parent content has independent native drawing authority under a hidden window.
        UIWindow independent = Window(Node("hidden with independent group", panels[17].Target));
        Image ownGroup = Graphic("independent original", independent.transform);
        CanvasGroup own = ownGroup.gameObject.AddComponent<CanvasGroup>(); own.ignoreParentGroups = true; own.alpha = .6f;
        CanvasConversion.LateTick();
        Check(!ownGroup.canvasRenderer.cull && Holds(ownGroup.canvasRenderer) == 0, "native ignoreParentGroups preserves original drawable content");

        // A live Off choice between two native panel callbacks must take the independent path.
        Proof.Reset(); MutateAfterFirstPanel(panels, () => PerfConfig.SharedUiWindowReadsOn = false);
        Check(Proof.RegistryScans >= 19, "option-off between native callbacks immediately resumes independent window reads");
        PerfConfig.SharedUiWindowReadsOn = true;
        CanvasConversion.LateTick();
        Check(replacementGraphic.canvasRenderer.cull && activatedGraphic!.canvasRenderer.cull, "re-enabled sharing keeps all original native visibility");

        // Ensure failure cleanup actually executes the production LateTick using scope.
        CanvasConversion.SeatCallback = panel => throw new InvalidOperationException("fixture native callback");
        try { CanvasConversion.LateTick(); throw new Exception("callback failure fixture did not throw"); }
        catch (InvalidOperationException) { }
        finally { CanvasConversion.SeatCallback = null; }
        Check(!(bool)Field("SharedVeilReadPass")! && ((ICollection)Field("SharedVeilRoots")!).Count == 0, "native callback exception releases the actual shared LateTick scope");
        foreach (ConvertedPanel panel in panels) Release(panel);
        Check(((IDictionary)Field("VeilHolds")!).Count == 0, "panel retirement releases every original renderer hold");
        // Retire the presentation context, then rebuild from the SAME live native originals.
        // No shared pass/old state may survive a reconnect-style local presentation reset.
        CanvasConversion.Active.Clear();
        foreach (ConvertedPanel panel in panels) { panel.Dormant = false; CanvasConversion.Active.Add(panel); }
        CanvasConversion.LateTick();
        Check(replacementGraphic.canvasRenderer.cull && newlyOpened!.canvasRenderer.cull && !pooled.canvasRenderer.cull, "cold presentation reset rebuilds exact current original window membership");
        Check(visible.sprite == sprite && visible.material == material && animated.GetComponent<CanvasGroup>().alpha == 1f, "cold reset preserves native current content and animation state");
        foreach (ConvertedPanel panel in panels) Release(panel);
        CanvasConversion.Active.Clear();
        UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(texture);
        Metrics = $"20 original panels / 100 original UIWindow registry entries; independent/shared member reads {independentMembers}/{sharedMembers}, group component reads {independentGroups}/{sharedGroups}; same-frame growth, equal-count replacement, reparenting, pooled graphics, all intermediate alphas and live Off; runtime supported={supported}";
    }
}
