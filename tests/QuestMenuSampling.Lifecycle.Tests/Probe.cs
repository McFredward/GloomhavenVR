using System;
using System.Collections;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Full production LoadingIndicator runs from Unity Update, across actual
// asynchronous Single scene activation and native asset collection. Native
// bootstrap/platform data loading itself is a controlled readiness boundary.
public sealed class LoadingLifecycleDriver : MonoBehaviour
{
    public Action? Tick;
    private void Update() { Tick?.Invoke(); }
}

public static class LoadingLifecycleProgram
{
    public static int ObservedBoundary { get; private set; }
    public static int RenderedSamples => shownFrames;
    public static int MenuFrameSamples => MenuFrameTrace.Samples;
    public static bool OriginalBootCallbacks { get; private set; }
    private static int checks, shownFrames;
    private static LoadingIndicator indicator = null!;
    private static LoadingLifecycleDriver driver = null!;
    private static Camera head = null!;
    private static RenderTexture target = null!;
    private static Action<int, Exception?> finish = null!;
    private static bool failed;
    private static GameObject? bootstrapOwner;
    private static int nativeTextureId;
    private static int nativeHideFlags;
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly System.Collections.Generic.List<string> lifecycleEdges = new();
    private static void ObserveLog(string message, string stack, LogType type)
    {
        if (message.StartsWith("[Quest startup] presentation spinner-edge ", StringComparison.Ordinal)) lifecycleEdges.Add(message);
    }
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new InvalidOperationException(message);
    }
    private static T Field<T>(string name) => (T)typeof(LoadingIndicator).GetField(name, Fields)!.GetValue(indicator)!;
    private static void NativeField(object owner, string name, object value) => owner.GetType().GetField(name, Fields)!.SetValue(owner, value);
    private static void Loading(SceneController scene, bool value) => typeof(SceneController).GetField("<IsLoading>k__BackingField", Fields)!.SetValue(scene, value);
    private static GameObject? Visual => Field<GameObject?>("_root");
    private static void AssertShown(string boundary, bool pixels = true)
    {
        GameObject? root = Visual;
        Check(root != null && root.activeInHierarchy && root.transform.parent == head.transform,
            "Actual Tick keeps the spinner attached and visible through " + boundary);
        Check(LoadingIndicator.FlatScreenSuppressed, "Actual boot/native loading owns suppression through " + boundary);
        foreach (Renderer renderer in root!.GetComponentsInChildren<Renderer>())
        {
            Check(renderer.enabled && renderer.sharedMaterial != null && renderer.sharedMaterial.mainTexture != null,
                "Actual Tick retains a live material/texture edge through " + boundary);
            Check((head.cullingMask & (1 << renderer.gameObject.layer)) != 0,
                "Actual head camera includes the spinner consumer through " + boundary);
        }
        // The production appear ramp deliberately begins at zero. Sample
        // visible pixels once it has a representable RGBA8 contribution;
        // ownership/suppression above are checked even during that ramp.
        if (pixels && Field<float>("_fade") >= .05f)
        {
            head.Render();
            if (QuestStandalonePlatform.Enabled)
            {
                Type witnessType = typeof(LoadingIndicator).GetNestedType("LoadingDrawWitness", BindingFlags.NonPublic)!;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                {
                    Component? witness = renderer.GetComponent(witnessType);
                    Check(witness != null && (int)witnessType.GetField("LastHeadId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(witness)! == head.GetInstanceID()
                        && (int)witnessType.GetField("LastHeadFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(witness)! == Time.frameCount,
                        "Actual native camera submission updates the owned loading draw witness through " + boundary);
                }
            }
            RenderTexture previous = RenderTexture.active;
            var copy = new Texture2D(96, 96, TextureFormat.RGBA32, false, false);
            try
            {
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, 96, 96), 0, 0, false); copy.Apply(false);
                float max = 0; int visible = 0, clear = 0;
                foreach (Color pixel in copy.GetPixels())
                {
                    max = Mathf.Max(max, pixel.maxColorComponent);
                    if (pixel.a > .01f) visible++; else clear++;
                }
                Check(max > .02f && visible > 3 && clear > 100,
                    "Actual Tick renders visible original spinner pixels through " + boundary);
                shownFrames++;
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(copy); }
        }
    }
    private static Camera NewHead()
    {
        var rig = new GameObject("fixture-head-rig"); UnityEngine.Object.DontDestroyOnLoad(rig);
        var owner = new GameObject("fixture-head-camera"); owner.transform.SetParent(rig.transform, false);
        Camera camera = owner.AddComponent<Camera>(); camera.enabled = false;
        camera.cullingMask = 1 << 29; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.fieldOfView = 35; camera.aspect = 1; camera.nearClipPlane = .05f; camera.farClipPlane = 10;
        camera.targetTexture = target; GloomhavenVR.Rig.VRRigDriver.HeadCamera = camera;
        return camera;
    }
    private static Image Icon(string name, Sprite sprite)
    {
        var owner = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Image image = owner.GetComponent<Image>(); image.sprite = sprite;
        image.rectTransform.sizeDelta = Vector2.one * 64; image.color = Color.white;
        return image;
    }
    private static GameObject IntroArt(bool active = true)
    {
        Sprite basis = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprite/LoadingBase.asset");
        Sprite overlay = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprite/LoadingOverlay.asset");
        Check(basis != null && overlay != null, "Actual owned native loading sprites import");
        // Preserve the actual Sprite's authored rect, tight packing, UVs and
        // padding. Sprite.Create(source.textureRect) silently replaced its
        // original128px rect with the trimmed atlas crop, hiding geometry bugs.
        // This fixture proves native collection occurs and the borrowed flags
        // stay unchanged; the imported assets can also be held by AssetDatabase,
        // so it does not claim that a particular asset would otherwise unload.
        Texture2D texture = basis!.texture;
        nativeTextureId = texture.GetInstanceID(); nativeHideFlags = (int)texture.hideFlags;
        Image baseImage = Icon("native-intro-base", basis);
        Image overlayImage = Icon("native-intro-overlay", overlay!);
        baseImage.color = new Color(.953f, .867f, .671f, 1);
        var owner = new GameObject("native-intro-spinner", typeof(RectTransform)); owner.SetActive(false);
        baseImage.transform.SetParent(owner.transform, false); overlayImage.transform.SetParent(owner.transform, false);
        var animation = owner.AddComponent<AnimateLoadingIcon>();
        NativeField(animation, "m_LoadingIconBase", baseImage.gameObject);
        NativeField(animation, "m_LoadingIconOverlay", overlayImage.gameObject);
        NativeField(animation, "m_IconSpinSpeed", -6f); NativeField(animation, "m_IconGlowSpeed", .07f);
        NativeField(animation, "m_IconUpdateSpeed", .07f); NativeField(animation, "m_IconOverlayMinAlpha", 0f);
        owner.SetActive(active);
        return owner;
    }

    private static IEnumerator OriginalBootstrap(Bootstrap bootstrap)
    {
        IntroPlayer? intro = null;
        void SeedIntro(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Intro") return;
            // Run the real Bootstrap.ShowSplash and IntroPlayer.ShowLogos
            // callback chain. Skip video/platform services and the logo list:
            // the empty-list case reaches the exact original loading edge.
            GameObject loading = IntroArt(false);
            var owner = new GameObject("original-native-intro-player"); owner.SetActive(false);
            intro = owner.AddComponent<IntroPlayer>(); intro.enabled = false;
            NativeField(intro, "_loading", loading);
            NativeField(intro, "_logos", new System.Collections.Generic.List<Sprite>());
            owner.SetActive(true);
        }
        SceneManager.sceneLoaded += SeedIntro;
        try
        {
            var splash = (IEnumerator)typeof(Bootstrap).GetMethod("ShowSplash", Fields)!.Invoke(bootstrap, null)!;
            bootstrap.StartCoroutine(splash);
            FieldInfo complete = typeof(IntroPlayer).GetField("EventCompleted", Fields)!;
            while (intro == null || complete.GetValue(intro) == null) yield return null;
            Check(Visual == null && !LoadingIndicator.FlatScreenSuppressed,
                "Actual native Bootstrap leaves the Intro uncovered before EventCompleted");
            Action callback = () => ((Action)complete.GetValue(intro)!).Invoke();
            intro.StartCoroutine((IEnumerator)typeof(IntroPlayer).GetMethod("ShowLogos", Fields)!.Invoke(intro, new object[] { callback })!);
            while (typeof(Bootstrap).GetField("_loadScene", Fields)!.GetValue(bootstrap) == null) yield return null;
            AsyncOperation load = (AsyncOperation)typeof(Bootstrap).GetField("_loadScene", Fields)!.GetValue(bootstrap)!;
            // Unlike the old fixture, never pause scene activation before
            // allowing production Tick to observe the real producer's field.
            yield return load; yield return null;
            Check(SceneManager.GetActiveScene().name == "Gloomhaven_unified",
                "Actual Bootstrap callback chain activates the native Single scene");
            OriginalBootCallbacks = true;
            AssertShown("original Bootstrap/Intro callback and immediate activation", false);
            while (Field<float>("_fade") < .5f) yield return null;
            AssertShown("original Bootstrap/Intro callback and immediate activation");
        }
        finally { SceneManager.sceneLoaded -= SeedIntro; }
    }
    private static SceneController NativeController()
    {
        // Do not invoke the unrelated native platform/services Awake. The real
        // component and its actual public loading state/serialized fields are
        // used by production Tick; the surrounding native loader owns the
        // transition timeline simulated below.
        var owner = new GameObject("controlled-native-scene-controller"); owner.SetActive(false);
        SceneController controller = owner.AddComponent<SceneController>(); SceneController.Instance = controller;
        Sprite basis = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprite/LoadingBase.asset");
        Sprite overlay = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprite/LoadingOverlay.asset");
        var spinner = new GameObject("controlled-native-loading-screen"); spinner.SetActive(false);
        LoadingScreen screen = spinner.AddComponent<LoadingScreen>(); controller.LoadingScreenInstance = screen;
        NativeField(screen, "m_LoadingIconBase", Icon("native-scenario-base", basis).gameObject);
        NativeField(screen, "m_LoadingIconOverlay", Icon("native-scenario-overlay", overlay).gameObject);
        NativeField(screen, "m_IconSpinSpeed", -6f); NativeField(screen, "m_IconGlowSpeed", .07f);
        NativeField(screen, "m_IconUpdateSpeed", .07f); NativeField(screen, "m_IconOverlayMinAlpha", 0f);
        return controller;
    }
    private static IEnumerator Sequence()
    {
        target = new RenderTexture(96, 96, 24, RenderTextureFormat.ARGB32); target.Create();
        head = NewHead();
        SceneController.Instance = null; GLOOM.MainMenu.MainMenuUIManager.Instance = null;
        indicator = new LoadingIndicator();
        driver.Tick = () => { if (!failed) indicator.Tick(); };
        bootstrapOwner = new GameObject("controlled-native-bootstrap");
        UnityEngine.Object.DontDestroyOnLoad(bootstrapOwner);
        var bootstrap = bootstrapOwner.AddComponent<Bootstrap>(); bootstrap.enabled = false;
        for (int i = 0; i < 3; i++)
        {
            yield return null;
            Check(Visual == null && !LoadingIndicator.FlatScreenSuppressed, "Actual Tick leaves the Intro uncovered before the native seam");
        }
        IEnumerator originalBoot = OriginalBootstrap(bootstrap);
        while (originalBoot.MoveNext()) yield return originalBoot.Current;
        AsyncOperation unified = SceneManager.LoadSceneAsync("Gloomhaven_unified", LoadSceneMode.Single);
        unified.allowSceneActivation = false; NativeField(bootstrap, "_loadScene", unified);
        for (int i = 0; i < 24; i++)
        {
            yield return new WaitForSecondsRealtime(.01f); AssertShown("original async scene preparation");
        }
        Check(!(Field<Material>("_baseMaterial").mainTexture is RenderTexture),
            "Quest boot uses the hardware-qualified native Sprite consumer without a copied RT");
        ObservedBoundary = 1;
        Check(Field<float>("_spinDegrees") == -6f && Field<float>("_stepSeconds") == .07f && Field<float>("_glowStep") == .07f,
            "Actual Tick preserves the original signed native spinner tuning");
        unified.allowSceneActivation = true; yield return unified; yield return null;
        Check(SceneManager.GetActiveScene().name == "Gloomhaven_unified", "Actual Single native scene activation completed");
        ObservedBoundary = 2;
        AsyncOperation collection = Resources.UnloadUnusedAssets(); yield return collection; yield return null;
        Check(Field<Material>("_baseMaterial").mainTexture.GetInstanceID() == nativeTextureId,
            "Persistent native renderer retains the exact original texture through actual asset collection");
        Check((int)Field<Material>("_baseMaterial").mainTexture.hideFlags == nativeHideFlags,
            "Quest native sprite retention never changes borrowed asset flags");
        AssertShown("Single scene activation and native asset collection");
        ObservedBoundary = 3;
        SceneController controller = NativeController();
        for (int i = 0; i < 12; i++)
        {
            yield return new WaitForSecondsRealtime(.01f); AssertShown("SceneController construction before native loading begins");
        }
        // Real Rig.Update tears down and immediately creates the next head;
        // WorldUI.Update reparents the still-live spinner before the deferred
        // Destroy of both old camera and rig. This differs from a headless gap.
        GameObject beforeSwap = Visual!;
        bool swapped = false;
        driver.Tick = () =>
        {
            if (!swapped)
            {
                swapped = true;
                GameObject oldRig = head.transform.parent.gameObject;
                UnityEngine.Object.Destroy(head.gameObject); UnityEngine.Object.Destroy(oldRig);
                head = NewHead();
                head.transform.parent.SetPositionAndRotation(new Vector3(3, 1, -5), Quaternion.Euler(0, 120, 0));
            }
            if (!failed) indicator.Tick();
        };
        yield return null;
        AssertShown("actual same-frame head-rig replacement");
        Check(Visual == beforeSwap, "The same persistent native spinner survives deferred head replacement");
        yield return null;
        AssertShown("deferred old head and rig destruction");
        Check(Visual == beforeSwap, "Deferred old-rig destruction leaves the reparented spinner intact");
        driver.Tick = () => { if (!failed) indicator.Tick(); };
        // Actual rig teardown destroys the old head and its child spinner.
        // Skip one frame without a head, then require Tick to rebuild naturally.
        UnityEngine.Object.Destroy(head.transform.parent.gameObject); GloomhavenVR.Rig.VRRigDriver.HeadCamera = null;
        yield return null;
        yield return Resources.UnloadUnusedAssets();
        head = NewHead(); yield return null;
        AssertShown("native head-rig reconstruction", false);
        while (Field<float>("_fade") < .5f)
        {
            yield return new WaitForSecondsRealtime(.01f); AssertShown("native head-rig reconstruction", false);
        }
        AssertShown("native head-rig reconstruction");
        ObservedBoundary = 4;
        Loading(controller, true);
        for (int i = 0; i < 10; i++)
        {
            yield return new WaitForSecondsRealtime(.01f); AssertShown("seamless ordinary native loading handover");
        }
        AsyncOperation menu = SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Additive); yield return menu; yield return null;
        AssertShown("actual additive MainMenu scene activation");
        ObservedBoundary = 5;
        IEnumerator handoff = NativeMenuHandoff(controller);
        while (handoff.MoveNext()) yield return handoff.Current;
        float until;
        ObservedBoundary = 6;
        Loading(controller, true);
        for (int i = 0; i < 12; i++) { yield return new WaitForSecondsRealtime(.01f); AssertShown("later native scenario loading"); }
        Check(!Field<bool>("_artIsProvisional") && Field<Material>("_baseMaterial").mainTexture is Texture2D,
            "Scenario loading keeps the established live native Sprite consumer");
        Check(Field<float>("_spinDegrees") == -6f,
            "Actual scenario Tick preserves native spinner direction");
        ObservedBoundary = 7;
        Loading(controller, false); until = Time.unscaledTime + .5f;
        while (Time.unscaledTime < until) yield return null;
        SceneController.Instance = null;
        for (int i = 0; i < 3; i++)
        {
            yield return null;
            Check(!LoadingIndicator.FlatScreenSuppressed && (Visual == null || !Visual.activeSelf),
                "Original scene teardown cannot resurrect completed boot coverage");
        }
        Check(shownFrames >= 50, "Actual Tick renders across boot, rig and native loading handovers");
        // A fresh desktop indicator receives the same completed native Intro
        // seam, but must retain the historical mod-owned RT and tuning policy.
        indicator.Shutdown(); QuestStandalonePlatform.Enabled = false;
        indicator = new LoadingIndicator(); GameObject desktopArt = IntroArt();
        for (int i = 0; i < 8; i++)
        {
            yield return new WaitForSecondsRealtime(.01f); AssertShown("unchanged desktop native boot");
        }
        Check(Field<Material>("_baseMaterial").mainTexture is RenderTexture owned && owned.IsCreated(),
            "Actual desktop boot retains its established owned GPU copy");
        Check(Field<float>("_spinDegrees") == 6f,
            "Actual desktop boot retains its established fallback tuning policy");
        Check(lifecycleEdges.Count == 4 && lifecycleEdges[0].Contains("edge=boot-armed")
            && lifecycleEdges[1].Contains("edge=boot-retired") && lifecycleEdges[1].Contains("seamless handover")
            && lifecycleEdges[2].Contains("edge=fully-faded-hide") && lifecycleEdges[3].Contains("edge=fully-faded-hide"),
            "Actual lifecycle evidence preserves the native retirement reason under the four-record process budget");
        Check(lifecycleEdges[1].Contains("baseDraw=(enabled=True,forceOff=False")
            && lifecycleEdges[1].Contains("view=") && lifecycleEdges[1].Contains("lastHead=")
            && lifecycleEdges[1].Contains("flip=") && lifecycleEdges[1].Contains("tint="),
            "The existing native takeover record includes drawable pose, material state and head submission evidence");
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("QUEST_LOADING_COMPOSITION_REPORT")))
        {
            indicator.Shutdown(); driver.Tick = null; UnityEngine.Object.DestroyImmediate(desktopArt);
            QuestStandalonePlatform.Enabled = true;
            IEnumerator nativeOverlay = NativeOverlayComposition.Probe(head, IntroArtForComposition, action => driver.Tick = action, Check);
            while (nativeOverlay.MoveNext()) yield return nativeOverlay.Current;
        }
        ObservedBoundary = 8;
    }
    private static bool SkipNativeLifecycle() => false;
    private static IEnumerator NativeMenuHandoff(SceneController controller)
    {
        Loading(controller, false);
        float until = Time.unscaledTime + .5f;
        while (Time.unscaledTime < until) yield return null;
        Check(!LoadingIndicator.FlatScreenSuppressed && Visual != null && Visual.activeSelf,
            "Actual Quest spinner covers native menu preparation after loading flags retire");
        // Native platform/GUIAnimator callbacks are outside this render-readiness
        // fixture. Keep the real component, original Initialised/IsOpen getters,
        // CanvasGroup and camera callbacks; seed only their authored state.
        var harmony = new Harmony("fixture.quest.menu.readiness");
        foreach (Type type in new[] { typeof(GLOOM.MainMenu.MainMenuUIManager), typeof(GLOOM.MainMenu.UIMainOptionsMenu), typeof(UIWindow) })
            foreach (string method in new[] { "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy" })
            {
                MethodInfo? callback = type.GetMethod(method, Fields | BindingFlags.DeclaredOnly);
                if (callback != null) harmony.Patch(callback, prefix: new HarmonyMethod(typeof(LoadingLifecycleProgram), nameof(SkipNativeLifecycle)));
            }
        var root = new GameObject("native-readiness-menu", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)); root.SetActive(false); root.layer = 5;
        var manager = root.AddComponent<GLOOM.MainMenu.MainMenuUIManager>(); manager.enabled = false;
        var optionsOwner = new GameObject("native-options", typeof(RectTransform)); optionsOwner.SetActive(false); optionsOwner.layer = 5;
        optionsOwner.transform.SetParent(root.transform, false);
        var options = optionsOwner.AddComponent<GLOOM.MainMenu.UIMainOptionsMenu>(); options.enabled = false;
        UIWindow window = optionsOwner.AddComponent<UIWindow>(); window.enabled = false;
        CanvasGroup group = optionsOwner.GetComponent<CanvasGroup>() ?? optionsOwner.AddComponent<CanvasGroup>(); group.alpha = 1;
        manager.mainMenu = options;
        GLOOM.MainMenu.MainMenuUIManager.Instance = manager;
        root.SetActive(true); optionsOwner.SetActive(true);
        var cameraOwner = new GameObject("native-readiness-ui-camera");
        Camera ui = cameraOwner.AddComponent<Camera>(); ui.enabled = false;
        ui.cullingMask = 1 << 5; ui.clearFlags = CameraClearFlags.SolidColor; ui.backgroundColor = Color.clear;
        var capture = new RenderTexture(64, 64, 0); capture.Create(); ui.targetTexture = capture;
        Canvas nativeCanvas = root.GetComponent<Canvas>(); nativeCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        nativeCanvas.worldCamera = ui; nativeCanvas.planeDistance = .5f;
        var graphicOwner = new GameObject("native-original-menu-graphic", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        graphicOwner.layer = 5;
        graphicOwner.transform.SetParent(optionsOwner.transform, false);
        graphicOwner.GetComponent<RectTransform>().sizeDelta = Vector2.one * 64;
        graphicOwner.GetComponent<Image>().color = Color.white;
        Canvas.ForceUpdateCanvases();
        var consumer = GameObject.CreatePrimitive(PrimitiveType.Quad);
        consumer.name = "native-readiness-capture-consumer"; consumer.layer = 29;
        consumer.transform.position = head.transform.TransformPoint(Vector3.forward * 2.5f);
        consumer.transform.rotation = head.transform.rotation;
        var material = new Material(Shader.Find("GloomhavenVR/Overlay")) { mainTexture = capture };
        material.SetFloat("_VertexColor", 0); material.SetFloat("_ZTest", 8);
        consumer.GetComponent<Renderer>().sharedMaterial = material;
        var screen = new FlatScreen(); screen.Setup(ui, capture, consumer);
        try
        {
            ui.Render(); head.Render(); yield return null;
            Check(!FlatScreen.QuestMenuPresented && Visual != null && Visual.activeSelf,
                "A menu state without original Show remains covered");
            NativeField(manager, "m_Initialised", true);
            NativeField(window, "m_CurrentVisualState", UIWindow.VisualState.Shown);
            graphicOwner.SetActive(false); ui.Render(); head.Render(); yield return null;
            Check(!FlatScreen.QuestMenuPresented && Visual != null && Visual.activeSelf,
                "An opened native menu without a submitted Graphic remains covered");
            graphicOwner.SetActive(true); Canvas.ForceUpdateCanvases();
            group.alpha = 0; ui.Render(); head.Render(); yield return null;
            Check(!FlatScreen.QuestMenuPresented, "An original opened menu with zero alpha remains covered");
            group.alpha = 1; root.GetComponent<CanvasGroup>().alpha = 0;
            Canvas.ForceUpdateCanvases(); ui.Render(); head.Render(); yield return null;
            Check(!FlatScreen.QuestMenuPresented, "A hidden native menu ancestor cannot acknowledge a visible picture");
            root.GetComponent<CanvasGroup>().alpha = 1; Canvas.ForceUpdateCanvases();
            group.alpha = 1; ui.Render(); yield return null;
            Check(!FlatScreen.QuestMenuPresented && Visual != null && Visual.activeSelf,
                "A produced menu without actual head submission remains covered");
            var foreign = new Texture2D(1, 1); material.mainTexture = foreign;
            ui.Render(); head.Render(); yield return null;
            Check(!FlatScreen.QuestMenuPresented, "A head surface displaying another texture cannot acknowledge the menu capture");
            material.mainTexture = capture; UnityEngine.Object.DestroyImmediate(foreign);
            Image nativeImage = graphicOwner.GetComponent<Image>();
            var blankTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            blankTexture.SetPixels(new Color[16]); blankTexture.Apply();
            Sprite transparent = Sprite.Create(blankTexture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f));
            try
            {
                if (MenuFrameTrace.Includes("transparent-native-graphic"))
                {
                    nativeImage.sprite = transparent; Canvas.ForceUpdateCanvases();
                    IEnumerator frames = MenuFrameTrace.Covered("transparent-native-graphic", ui, head, capture, target, Visual!, Check);
                    while (frames.MoveNext()) yield return frames.Current;
                    nativeImage.sprite = null;
                }
                if (MenuFrameTrace.Includes("offscreen-native-graphic"))
                {
                    nativeImage.rectTransform.anchoredPosition = Vector2.one * 100000; Canvas.ForceUpdateCanvases();
                    IEnumerator frames = MenuFrameTrace.Covered("offscreen-native-graphic", ui, head, capture, target, Visual!, Check);
                    while (frames.MoveNext()) yield return frames.Current;
                    nativeImage.rectTransform.anchoredPosition = Vector2.zero;
                }
                if (MenuFrameTrace.Includes("later-target-overwrite"))
                {
                    var laterOwner = new GameObject("native-later-capture-writer");
                    Camera later = laterOwner.AddComponent<Camera>(); later.enabled = false;
                    later.targetTexture = capture; later.cullingMask = 0; later.clearFlags = CameraClearFlags.SolidColor; later.backgroundColor = Color.clear;
                    try
                    {
                        Canvas.ForceUpdateCanvases();
                        IEnumerator frames = MenuFrameTrace.Covered("later-target-overwrite", ui, head, capture, target, Visual!, Check, later);
                        while (frames.MoveNext()) yield return frames.Current;
                    }
                    finally { UnityEngine.Object.DestroyImmediate(laterOwner); }
                }
                if (MenuFrameTrace.Includes("black-head-consumer"))
                {
                    material.color = Color.black; Canvas.ForceUpdateCanvases();
                    IEnumerator frames = MenuFrameTrace.Covered("black-head-consumer", ui, head, capture, target, Visual!, Check);
                    while (frames.MoveNext()) yield return frames.Current;
                    material.color = Color.white;
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(transparent); UnityEngine.Object.DestroyImmediate(blankTexture); }
            ui.Render(); head.Render(); yield return null;
            Check(FlatScreen.QuestMenuPresented, "Actual produced visible native UI reaches the real head consumer");
            MenuFrameTrace.Visible("visible-native-menu", ui, head, capture, target, Visual!, Check);
            until = Time.unscaledTime + .5f;
            while (Time.unscaledTime < until) yield return null;
            Check(!LoadingIndicator.FlatScreenSuppressed && (Visual == null || !Visual.activeSelf),
                "Actual menu presentation releases only the Quest spinner through its original fade");
            var other = new GameObject("other-native-menu"); other.SetActive(false);
            var replacement = other.AddComponent<GLOOM.MainMenu.MainMenuUIManager>();
            GLOOM.MainMenu.MainMenuUIManager.Instance = replacement;
            Check(!FlatScreen.QuestMenuPresented, "A new native menu cannot inherit an old presentation receipt");
            UnityEngine.Object.DestroyImmediate(other);
        }
        finally
        {
            screen.Stop(); GLOOM.MainMenu.MainMenuUIManager.Instance = null;
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(consumer);
            UnityEngine.Object.DestroyImmediate(cameraOwner); UnityEngine.Object.DestroyImmediate(material);
            capture.Release(); UnityEngine.Object.DestroyImmediate(capture); harmony.UnpatchSelf();
        }
    }

    private static GameObject IntroArtForComposition() => IntroArt();
    public static void Start(Action<int, Exception?> completion)
    {
        checks = shownFrames = 0; ObservedBoundary = 0; OriginalBootCallbacks = false; failed = false; finish = completion;
        lifecycleEdges.Clear(); Application.logMessageReceived += ObserveLog;
        var owner = new GameObject("full-production-loading-tick-driver"); UnityEngine.Object.DontDestroyOnLoad(owner);
        driver = owner.AddComponent<LoadingLifecycleDriver>();
        driver.StartCoroutine(Guarded(Sequence()));
    }
    private static IEnumerator Guarded(IEnumerator sequence)
    {
        while (true)
        {
            object? current;
            try { if (!sequence.MoveNext()) break; current = sequence.Current; }
            catch (Exception error) { Complete(error); yield break; }
            yield return current;
        }
        Complete(null);
    }
    private static void Complete(Exception? error)
    {
        failed = true; driver.Tick = null;
        indicator?.Shutdown(); GloomhavenVR.Rig.VRRigDriver.HeadCamera = null;
        SceneController.Instance = null; GLOOM.MainMenu.MainMenuUIManager.Instance = null;
        if (head != null) { head.targetTexture = null; UnityEngine.Object.Destroy(head.transform.parent.gameObject); }
        if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
        if (bootstrapOwner != null) UnityEngine.Object.Destroy(bootstrapOwner);
        UnityEngine.Object.Destroy(driver.gameObject);
        Application.logMessageReceived -= ObserveLog;
        finish(checks, error);
    }
}
