using System;
using System.Collections;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
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
        var owner = new GameObject("fixture-head-rig"); UnityEngine.Object.DontDestroyOnLoad(owner);
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
        // Clone only the bounded atlas used by these original sprites. Neither
        // the canonical source nor its import metadata is modified. The clone
        // has no DontUnload flag or static Texture/Sprite reference.
        Texture2D texture = UnityEngine.Object.Instantiate(basis!.texture);
        texture.name = "fixture-native-intro-atlas"; texture.hideFlags = HideFlags.None;
        nativeTextureId = texture.GetInstanceID(); nativeHideFlags = (int)texture.hideFlags;
        Sprite clone(Sprite source) => Sprite.Create(texture, source.textureRect, new Vector2(.5f, .5f), 100);
        Image baseImage = Icon("native-intro-base", clone(basis));
        Image overlayImage = Icon("native-intro-overlay", clone(overlay!));
        baseImage.color = new Color(.953f, .867f, .671f, 1);
        var owner = new GameObject("native-intro-spinner"); owner.SetActive(false);
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
        // Actual rig teardown destroys the old head and its child spinner.
        // Skip one frame without a head, then require Tick to rebuild naturally.
        UnityEngine.Object.Destroy(head.gameObject); GloomhavenVR.Rig.VRRigDriver.HeadCamera = null;
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
        Loading(controller, false);
        float until = Time.unscaledTime + .5f;
        while (Time.unscaledTime < until) yield return null;
        Check(!LoadingIndicator.FlatScreenSuppressed && (Visual == null || !Visual.activeSelf),
            "Actual native completion releases suppression and fades the spinner away");
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
        indicator = new LoadingIndicator(); IntroArt();
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
        ObservedBoundary = 8;
    }
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
        if (head != null) { head.targetTexture = null; UnityEngine.Object.Destroy(head.gameObject); }
        if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
        if (bootstrapOwner != null) UnityEngine.Object.Destroy(bootstrapOwner);
        UnityEngine.Object.Destroy(driver.gameObject);
        Application.logMessageReceived -= ObserveLog;
        finish(checks, error);
    }
}
