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

// Intro.unity's actual _loading root is a screen-space overlay, with a black
// full-screen Image before its two64px icons205px above the bottom edge. A
// camera-only RenderTexture fixture cannot observe this later native UI draw.
internal static class NativeOverlayComposition
{
    private static readonly BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
    private static GameObject Visual(LoadingIndicator value) => (GameObject)typeof(LoadingIndicator).GetField("_root", Fields)!.GetValue(value)!;
    internal static IEnumerator Probe(Camera head, Func<GameObject> art, Action<Action?> setTick, Action<bool, string> check)
    {
        RenderTexture saved = head.targetTexture; bool enabled = head.enabled;
        LoadingIndicator? indicator = null;
        GameObject? owner = null, actor = null, unrelated = null;
        int callbacks = 0;
        try
        {
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"), false, "Game").Show();
            head.targetTexture = null; head.enabled = true; head.rect = new Rect(0, 0, 1, 1);
            owner = Graph(art(), out Canvas canvas);
            Scene introScene = SceneManager.GetSceneByName("Intro");
            if (!introScene.IsValid() || !introScene.isLoaded) introScene = SceneManager.CreateScene("Intro");
            SceneManager.MoveGameObjectToScene(owner, introScene);
            actor = new GameObject("native-original-IntroPlayer"); actor.SetActive(false);
            SceneManager.MoveGameObjectToScene(actor, introScene);
            IntroPlayer intro = actor.AddComponent<IntroPlayer>(); intro.enabled = false;
            typeof(IntroPlayer).GetField("_loading", Fields)!.SetValue(intro, owner);
            typeof(IntroPlayer).GetField("_logos", Fields)!.SetValue(intro, new System.Collections.Generic.List<Sprite>());
            actor.SetActive(true);
            intro.EventCompleted += () => callbacks++;
            Action completed = () => ((Action)typeof(IntroPlayer).GetField("EventCompleted", Fields)!.GetValue(intro)!).Invoke();
            intro.StartCoroutine((IEnumerator)typeof(IntroPlayer).GetMethod("ShowLogos", Fields)!.Invoke(intro, new object[] { completed })!);
            yield return null;
            check(callbacks == 1 && owner.activeInHierarchy && canvas.enabled,
                "Actual original IntroPlayer callback enables its serialized native overlay before Quest admission");
            unrelated = new GameObject("unrelated-overlay-must-stay-native", typeof(RectTransform), typeof(Canvas));
            Canvas other = unrelated.GetComponent<Canvas>(); other.renderMode = RenderMode.ScreenSpaceOverlay; other.sortingOrder = 1001;
            // B691's exact capture has a live native loading animator but no
            // admitted Intro canvas. Model the original actor having departed
            // after delivering its one completion, with the loading root live.
            UnityEngine.Object.DestroyImmediate(actor); actor = null;
            // The existing modal/native camera binding can legitimately change
            // this exact root's mode before loading art is acquired. Ownership
            // and the black backdrop stay the same; an Overlay-only predicate
            // would silently leave that later draw covering the VR consumer.
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = head;
            canvas.planeDistance = 1f;
            indicator = new LoadingIndicator(); setTick(indicator.Tick);
            for (int i = 0; i < 24; i++) yield return null;
            Renderer spinner = Visual(indicator).GetComponentInChildren<Renderer>();
            bool suppressed = !canvas.enabled;
            check(Visual(indicator).activeInHierarchy && owner.activeInHierarchy
                && owner.GetComponentInChildren<AnimateLoadingIcon>().isActiveAndEnabled && callbacks == 1 && other.enabled,
                "Quest admission retains native animator, callback, active owner and unrelated canvas");
            check(suppressed, "Exact Intro loading owner hands off after a native camera rebind");
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Causal old-graph observation: leave the same spinner/texture/pose
            // live, but temporarily reintroduce the authored flat overlay.
            setTick(null); canvas.enabled = true;
            for (int i = 0; i < 6; i++) yield return null;
            float covered = Capture(head, spinner, "native-overlay-covered");
            setTick(indicator.Tick);
            for (int i = 0; i < 6; i++) yield return null;
            float uncovered = Capture(head, spinner, "native-overlay-owned");
            UnityEngine.Debug.Log("[Lifecycle] final overlay composition covered=" + covered + " uncovered=" + uncovered + " screen=" + Screen.width + "x" + Screen.height);
            check(suppressed && covered < .01f && uncovered > .02f && !canvas.enabled,
                "Actual final native overlay draw hides the admitted spinner and Quest Tick reveals the same pixels");
            WorldUIConfig.LoadingIndicator.Value = false;
            float until = Time.unscaledTime + .5f; while (Time.unscaledTime < until) yield return null;
            check(canvas.enabled && callbacks == 1 && other.enabled,
                "Disabling the indicator restores only its original native Intro canvas");
            WorldUIConfig.LoadingIndicator.Value = true;
            for (int i = 0; i < 6; i++) yield return null;
            check(!canvas.enabled, "Quest boot can resume presentation after an indicator option toggle");
            indicator.Shutdown(); setTick(null);
            check(canvas.enabled && callbacks == 1, "Driver shutdown restores the captured native canvas without replaying callbacks");
            indicator = new LoadingIndicator(); setTick(indicator.Tick);
            for (int i = 0; i < 6; i++) yield return null;
            // Single activation must actually destroy the exact source root,
            // rather than assuming that it persists for the entire black gap.
            AsyncOperation single = SceneManager.LoadSceneAsync("Gloomhaven_unified", LoadSceneMode.Single); yield return single; yield return null;
            check(owner == null && actor == null && other == null,
                "Actual Single scene unload destroys the ordinary Intro overlay and actor");
            for (int i = 0; i < 6; i++) yield return null;
            spinner = Visual(indicator).GetComponentInChildren<Renderer>();
            check(Visual(indicator).activeInHierarchy && Capture(head, spinner, "native-overlay-after-single") > .02f,
                "The same native-art consumer stays drawable after actual overlay destruction and Single activation");
            // The native Single replacement has its OWN opaque final overlay.
            // Reproduce the exact GlobalCanvas -> LoadingScreenGO producer edge
            // while the same persistent boot spinner continues across handover.
            owner = Graph(art(), out canvas); owner.SetActive(true);
            GameObject loading = owner.transform.GetChild(0).gameObject;
            actor = new GameObject("controlled-unified-native-controller"); actor.SetActive(false);
            SceneController controller = actor.AddComponent<SceneController>();
            SceneController.Instance = controller;
            controller.GlobalCanvas = owner; controller.LoadingScreenGO = loading;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = head; canvas.planeDistance = 1f;
            typeof(SceneController).GetField("<IsLoading>k__BackingField", Fields)!.SetValue(controller, true);
            var hidden = new GameObject("native-already-disabled-image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            hidden.transform.SetParent(loading.transform, false); hidden.GetComponent<Image>().enabled = false;
            var sibling = new GameObject("native-error-sibling-must-stay-enabled", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            sibling.transform.SetParent(owner.transform, false); sibling.GetComponent<Image>().color = Color.clear;
            for (int i = 0; i < 12; i++) yield return null;
            check(canvas.enabled && loading.activeInHierarchy && loading.GetComponentInChildren<AnimateLoadingIcon>().isActiveAndEnabled
                && sibling.GetComponent<Image>().enabled && !hidden.GetComponent<Image>().enabled,
                "Native loading handoff preserves GlobalCanvas siblings, original animation and pre-disabled graphics");
            check(!loading.GetComponent<Image>().enabled,
                "Exact native loading widget hands off after a native camera rebind");
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            setTick(null);
            foreach (Graphic graphic in loading.GetComponentsInChildren<Graphic>(true))
                if (graphic.gameObject != hidden) graphic.enabled = true;
            for (int i = 0; i < 6; i++) yield return null;
            covered = Capture(head, spinner, "unified-native-loading-covered");
            setTick(indicator.Tick);
            for (int i = 0; i < 6; i++) yield return null;
            uncovered = Capture(head, spinner, "unified-native-loading-owned");
            check(canvas.enabled && covered < .01f && uncovered > .02f,
                "Actual unified native loading overlay covers the admitted spinner until its exact widget hands off graphics");
            typeof(SceneController).GetField("<IsLoading>k__BackingField", Fields)!.SetValue(controller, false);
            float release = Time.unscaledTime + .5f; while (Time.unscaledTime < release) yield return null;
            check(canvas.enabled && loading.GetComponent<Image>().enabled && !hidden.GetComponent<Image>().enabled
                && sibling.GetComponent<Image>().enabled && !LoadingIndicator.FlatScreenSuppressed,
                "Native loading completion restores exact prior graphic bits and leaves native sibling presentation intact");
            SceneController.Instance = null;
            UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(actor); owner = actor = null;
            indicator.Shutdown(); setTick(null); indicator = null;
            // A complete source graph also exists on desktop. Quest's capture
            // must not take its Canvas, even when boot and art otherwise match.
            QuestStandalonePlatform.Enabled = false;
            owner = Graph(art(), out canvas); owner.SetActive(true);
            actor = new GameObject("desktop-original-IntroPlayer"); actor.SetActive(false);
            intro = actor.AddComponent<IntroPlayer>(); intro.enabled = false;
            typeof(IntroPlayer).GetField("_loading", Fields)!.SetValue(intro, owner); actor.SetActive(true);
            indicator = new LoadingIndicator(); setTick(indicator.Tick);
            for (int i = 0; i < 6; i++) yield return null;
            check(canvas.enabled, "Desktop retains the original native overlay canvas throughout the same boot clause");
            indicator.Shutdown(); setTick(null);
        }
        finally
        {
            setTick(null); indicator?.Shutdown(); SceneController.Instance = null; WorldUIConfig.LoadingIndicator.Value = true; QuestStandalonePlatform.Enabled = true;
            head.targetTexture = saved; head.enabled = enabled;
            if (owner != null) UnityEngine.Object.Destroy(owner);
            if (actor != null) UnityEngine.Object.Destroy(actor);
            if (unrelated != null) UnityEngine.Object.Destroy(unrelated);
        }
    }

    private static GameObject Graph(GameObject art, out Canvas canvas)
    {
        var owner = new GameObject("PersistentOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)); owner.SetActive(false);
        canvas = owner.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
        var scaler = owner.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        var panel = new GameObject("LoadingScreen_unified", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); panel.transform.SetParent(owner.transform, false);
        RectTransform rect = panel.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
        panel.GetComponent<Image>().color = Color.black;
        art.transform.SetParent(panel.transform, false);
        RectTransform holder = art.GetComponent<RectTransform>(); holder.anchorMin = Vector2.zero; holder.anchorMax = Vector2.one; holder.sizeDelta = Vector2.zero;
        foreach (Image image in art.GetComponentsInChildren<Image>(true))
        {
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(.5f, 0);
            image.rectTransform.anchoredPosition = new Vector2(0, 205);
            image.rectTransform.sizeDelta = Vector2.one * 64;
        }
        return owner;
    }

    private static float Capture(Camera head, Renderer spinner, string role)
    {
        RenderTexture.active = null;
        Texture2D capture = ScreenCapture.CaptureScreenshotAsTexture();
        if (capture == null) throw new InvalidOperationException("Final native overlay screenshot unavailable");
        try
        {
            string? output = Environment.GetEnvironmentVariable("QUEST_LOADING_COMPOSITION_REPORT");
            if (!string.IsNullOrEmpty(output)) File.WriteAllBytes(Path.Combine(output, role + ".png"), capture.EncodeToPNG());
            Vector3 center = head.WorldToViewportPoint(spinner.bounds.center);
            int cx = Mathf.RoundToInt(center.x * capture.width), cy = Mathf.RoundToInt(center.y * capture.height);
            float maximum = 0;
            // Upper half of the admitted world-space disc, excluding the
            // original much-lower bottom-anchored flat native64px icons.
            for (int y = Math.Max(0, cy); y < Math.Min(capture.height, cy + capture.height / 5); y++)
                for (int x = Math.Max(0, cx - capture.width / 5); x < Math.Min(capture.width, cx + capture.width / 5); x++)
                    maximum = Mathf.Max(maximum, capture.GetPixel(x, y).maxColorComponent);
            return maximum;
        }
        finally { UnityEngine.Object.Destroy(capture); }
    }
}
