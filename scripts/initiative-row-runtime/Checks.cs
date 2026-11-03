using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;
using UnityEngine.UI;

public static class InitiativeRowChecks
{
    public static int Assertions;
    private static string evidence = "";
    private static void Check(bool value, string why)
    {
        Assertions++;
        if (!value) throw new Exception("Initiative row assertion: " + why);
    }

    private sealed class Scene : IDisposable
    {
        internal readonly List<GameObject> Owned = new();
        internal readonly List<InitiativeTrackActorBehaviour> Entries = new();
        internal readonly Dictionary<Transform, float> HomeY = new();
        internal readonly Transform Board, NativeHome;
        internal readonly InitiativeTrack Track;
        internal readonly RectTransform Holder;
        internal readonly InitiativeTrackSurface Surface = new();
        internal readonly Camera Camera;
        internal int BatchCreatedFrame = -1;
        private readonly Material ink;
        private GameObject? backdrop;
        internal Scene(int count, float scale = 1.93f)
        {
            NativeHome = Go("OriginalNativeCanvas").transform;
            NativeHome.localScale = Vector3.one * .003f;
            Board = Go("ManualBoard").transform;
            Board.SetPositionAndRotation(new Vector3(.4f, 1.1f, 2), Quaternion.Euler(18, 47, 8));
            Board.localScale = Vector3.one * scale;
            var mount = Go("InitiativeMount").transform; mount.SetParent(Board, false);
            mount.localPosition = new Vector3(0, .17f, -.009f);
            PlayTray.Current = new PlayTray { InitiativeMount = mount };
            var target = Rect("InitiativeModule", NativeHome); target.sizeDelta = new Vector2(800, 180);
            Track = target.gameObject.AddComponent<InitiativeTrack>(); InitiativeTrack.Instance = Track;
            Holder = Rect("InitiativeTrack", target); Holder.sizeDelta = new Vector2(800, 180);
            Track.initiativeTrackHolder = Holder;
            Track.layoutGroup = Holder.gameObject.AddComponent<HorizontalLayoutGroup>();
            Track.layoutGroup.childAlignment = TextAnchor.MiddleCenter;
            Track.layoutGroup.padding.top = 15; Track.layoutGroup.spacing = 25;
            Track.layoutGroup.childControlWidth = Track.layoutGroup.childControlHeight = false;
            Track.layoutGroup.childForceExpandWidth = Track.layoutGroup.childForceExpandHeight = false;
            Track.contentSizeFitter = Holder.gameObject.AddComponent<ContentSizeFitter>();
            Track.contentSizeFitter.horizontalFit = Track.contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ink = new Material(Shader.Find("GloomhavenVR/Overlay")); ink.color = Color.white;
            ink.SetInt("_ZWrite", 0); ink.SetInt("_ZTest", 4); ink.SetInt("_Cull", 0);
            for (int i = 0; i < count; i++) Add(i + 1);
            NativeLayout();
            foreach (var entry in Entries) HomeY[entry.transform] = entry.transform.localPosition.y;
            Choreographer.s_Choreographer = new Choreographer();
            Camera = Go("FinalHead").AddComponent<Camera>(); Camera.orthographic = true;
            Camera.orthographicSize = .25f * scale; Camera.nearClipPlane = .001f * scale;
            Camera.farClipPlane = 10 * scale; Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Color.black;
        }
        private GameObject Go(string name)
        { var go = new GameObject(name); Owned.Add(go); return go; }
        private RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)); Owned.Add(go);
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; return rect;
        }
        internal InitiativeTrackActorBehaviour Add(int order)
        {
            var root = Rect("NativeEntry" + order, Holder); root.sizeDelta = new Vector2(150, 130);
            var entry = root.gameObject.AddComponent<InitiativeTrackActorBehaviour>(); entry.Order = order;
            entry.avatarButton = root.gameObject.AddComponent<Button>();
            entry.avatarButton.onClick.AddListener(() => entry.Clicks++);
            var image = Rect("OriginalPortrait", root); image.sizeDelta = new Vector2(130, 110);
            image.localPosition = new Vector3(0, 0, -11); // nested native styling, not the entry plane
            var graphic = image.gameObject.AddComponent<Image>(); graphic.color = Color.red; graphic.material = ink;
            Entries.Add(entry); Track.actorsUI.Add(entry); return entry;
        }
        internal void Remove(InitiativeTrackActorBehaviour entry)
        { entry.gameObject.SetActive(false); Entries.Remove(entry); Track.actorsUI.Remove(entry); }
        internal void NativeLayout()
        {
            Track.layoutGroup!.enabled = Track.contentSizeFitter!.enabled = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(Holder);
            Canvas.ForceUpdateCanvases();
        }
        internal void Convert()
        {
            Surface.Tick();
            var host = Surface.Current!;
            host.HostCanvas.worldCamera = Camera;
            var center = host.HostTransform.position;
            var headOffset = (-host.HostTransform.forward * .6f + host.HostTransform.right * .14f) * Board.lossyScale.x;
            Camera.transform.SetPositionAndRotation(center + headOffset,
                Quaternion.LookRotation(-headOffset, host.HostTransform.up));
            backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad); Owned.Add(backdrop);
            backdrop.name = "IndependentEntryPlaneWitness"; backdrop.transform.SetParent(host.HostTransform, false);
            backdrop.transform.localPosition = new Vector3(0, 0, -9);
            backdrop.transform.localScale = new Vector3(2400, 1000, 1);
            var blue = new Material(Shader.Find("Unlit/Color")); blue.color = Color.blue;
            backdrop.GetComponent<Renderer>().sharedMaterial = blue;
        }
        internal void Tick()
        {
            var pose = Board.localToWorldMatrix;
            Surface.Tick(); Surface.LateTick();
            Check(MatrixDistance(pose, Board.localToWorldMatrix) < 1e-6f,
                "actual surface/slide never moves or rescales the manually owned board");
        }
        internal void NativeBatch()
        {
            // The original native controller's exact world-X producer/cancellation API.
            // Actor membership/selection is an explicit data port; no gameplay controller runs.
            Track.layoutGroup!.enabled = Track.contentSizeFitter!.enabled = false;
            var locations = Entries.OrderBy(entry => entry.transform.GetSiblingIndex())
                .Select(entry => entry.transform.position.x).ToArray();
            Track.actorsUI.Sort((a, b) => a.Order.CompareTo(b.Order));
            foreach (var entry in Track.actorsUI) entry.transform.SetAsFirstSibling();
            Track.moveXAnimations.Clear();
            int i = 0;
            foreach (var entry in Track.actorsUI)
                Track.moveXAnimations.Add(LeanTween.moveX(entry.gameObject, locations[locations.Length - 1 - i++],
                    Track.trackReorderDuration).setEase(LeanTweenType.easeOutQuad));
        }
        internal void ScheduleNativeBatch()
        {
            BatchCreatedFrame = -1;
            Track.isAnimating = true;
            Track.layoutGroup!.enabled = Track.contentSizeFitter!.enabled = false;
            // Explicit EOF phase port: Unity does not service WaitForEndOfFrame in
            // Editor batchmode. The source-bound native coroutine:258–287 produces
            // this batch after the preceding coordinator LateTick. The next real
            // frame executes original LeanTween IL before the following LateTick.
            NativeBatch();
            BatchCreatedFrame = Time.frameCount;
            File.AppendAllText(Path.Combine(evidence, "native-schedule.txt"),
                "source-bound EOF phase port at frame " + BatchCreatedFrame + "\n");
        }
        internal Dictionary<Transform, float> LayoutOracle()
        {
            // A separate real Unity layout computes final X slots without touching the adopted row.
            var oracle = Rect("IndependentNativeLayout", NativeHome);
            var group = oracle.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.childAlignment = Track.layoutGroup!.childAlignment; group.padding = Track.layoutGroup.padding;
            group.spacing = Track.layoutGroup.spacing;
            group.childControlWidth = group.childControlHeight = false;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
            group.reverseArrangement = Track.layoutGroup.reverseArrangement;
            var fitter = oracle.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var copies = new Dictionary<Transform, RectTransform>();
            foreach (var entry in Entries.OrderBy(entry => entry.transform.GetSiblingIndex()))
            {
                var copy = Rect(entry.name, oracle); copy.sizeDelta = ((RectTransform)entry.transform).rect.size;
                copies[entry.transform] = copy;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(oracle); Canvas.ForceUpdateCanvases();
            return copies.ToDictionary(pair => pair.Key, pair => pair.Value.localPosition.x);
        }
        internal void PlaneAndPixels(string label)
        {
            Canvas.ForceUpdateCanvases();
            var host = Surface.Current!;
            var render = new RenderTexture(768, 480, 24); Camera.targetTexture = render; Camera.Render();
            var before = RenderTexture.active; RenderTexture.active = render;
            var pixels = new Texture2D(render.width, render.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0); pixels.Apply();
            int missing = 0; float worst = 0, planeMeters = 0;
            foreach (var entry in Entries)
            {
                var root = entry.transform;
                worst = Mathf.Max(worst, Mathf.Abs(root.localPosition.z));
                var canonical = Holder.TransformPoint(new Vector3(root.localPosition.x, root.localPosition.y, 0));
                planeMeters = Mathf.Max(planeMeters, Vector3.Distance(root.position, canonical) / Board.lossyScale.x);
                var portrait = root.GetChild(0);
                // Query the independent canonical plane; a leaked positiveZ lies behind the blue witness.
                var expected = Holder.TransformPoint(new Vector3(root.localPosition.x, root.localPosition.y, -11));
                var screen = Camera.WorldToScreenPoint(expected);
                int x = Mathf.RoundToInt(screen.x), y = Mathf.RoundToInt(screen.y);
                bool inside = x > 1 && y > 1 && x < pixels.width - 2 && y < pixels.height - 2;
                Color color = inside ? pixels.GetPixel(x, y) : Color.black;
                if (!inside || color.r < .7f || color.b > .3f) missing++;
                Check(Mathf.Abs(portrait.localPosition.z + 11) < .001f,
                    "nested native portrait styling survives entry-plane repair");
            }
            File.WriteAllBytes(Path.Combine(evidence, label + ".png"), pixels.EncodeToPNG());
            File.WriteAllText(Path.Combine(evidence, label + ".txt"),
                "worst rootZ=" + worst + " px; physical plane error=" + planeMeters + " m; missing red portrait pixels=" + missing);
            RenderTexture.active = before; Camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(pixels); render.Release(); UnityEngine.Object.DestroyImmediate(render);
            Check(worst < .01f && planeMeters < .00001f && missing == 0,
                "native entry plane and final pixels restored after " + label +
                "; rootZ=" + worst + " px, plane=" + planeMeters + " m, missing=" + missing);
        }
        public void Dispose()
        {
            Surface.Shutdown();
            foreach (var entry in Entries) if (entry != null) entry.avatarButton!.onClick.Invoke();
            Check(Entries.All(entry => entry == null || entry.Clicks == 1), "original actor button callbacks remain usable");
            foreach (var go in Owned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(ink);
            InitiativeTrack.Instance = null!; PlayTray.Current = null; Choreographer.s_Choreographer = null;
            LeanTween.reset();
        }
    }

    private static float MatrixDistance(Matrix4x4 a, Matrix4x4 b)
    { float max = 0; for (int i = 0; i < 16; i++) max = Mathf.Max(max, Mathf.Abs(a[i] - b[i])); return max; }

    public static IEnumerator Run(string output, string scenario)
    {
        evidence = output; Assertions = 0; VRLog.Lines.Clear();
        if (scenario == "cold") { var cold = Cold(); while (cold.MoveNext()) yield return cold.Current; yield break; }
        if (scenario == "idle" || scenario == "abort")
        { var idle = Idle(scenario == "abort"); while (idle.MoveNext()) yield return idle.Current; yield break; }
        var changed = Changed(scenario == "remove", scenario == "paused");
        while (changed.MoveNext()) yield return changed.Current;
    }

    private static IEnumerator Changed(bool remove, bool paused)
    {
        using var scene = new Scene(4);
        scene.Convert(); scene.Track.isAnimating = true; scene.Tick();
        scene.ScheduleNativeBatch();
        var native = ThroughNativeUpdate(scene); while (native.MoveNext()) yield return native.Current;
        scene.Tick();
        Check(scene.Surface.Sliding, "actual production slide adopts the first native tween batch");
        yield return null; scene.Tick();
        scene.Track.isAnimating = false; scene.Track.moveXAnimations.Clear();
        yield return null; scene.Tick();
        var from = scene.Entries.ToDictionary(entry => entry.transform, entry => entry.transform.localPosition.x);
        // New card initiatives reorder the still-visible original cohort. A second
        // identical sort would correctly create no foreign movement and no witness.
        foreach (var entry in scene.Track.actorsUI) entry.Order = 10 - entry.Order;
        scene.ScheduleNativeBatch();
        native = ThroughNativeUpdate(scene); while (native.MoveNext()) yield return native.Current;
        float foreign = scene.Entries.Max(entry => Mathf.Abs(entry.transform.localPosition.z));
        Check(foreign > 2, "actual native LeanTween world-X IL produced measurable foreign localZ before changed-row takeover");
        // Choreographer.Update→ProcessMessage→NormalizeActorsPool can run after the
        // native tween Update and before our LateTick; it does not retire that batch.
        if (remove) scene.Remove(scene.Entries[1]); else scene.Add(9);
        File.AppendAllText(Path.Combine(evidence, "native-schedule.txt"),
            "native first Update and pool mutation before coordinator Late frame " + Time.frameCount + "\n");
        var destinations = scene.LayoutOracle();
        scene.Tick();
        Check(VRLog.Lines.Any(line => line.Contains("CHANGED ROW")) || !scene.Surface.Sliding,
            "the changed-entry-set production branch is reached");
        scene.PlaneAndPixels(remove ? "changed-remove" : "changed-add");
        float began = Time.unscaledTime;
        if (paused) Time.timeScale = 0;
        int samples = 0;
        while (scene.Surface.Sliding)
        {
            yield return null;
            // An explicit user carry/resize of the board stays authoritative during the slide.
            scene.Board.position += new Vector3(.0002f, 0, 0);
            scene.Board.localScale *= 1.0001f;
            scene.Tick(); samples++;
            float elapsed = Time.unscaledTime - began;
            float t = Mathf.Clamp01(elapsed / scene.Track.trackReorderDuration);
            foreach (var entry in scene.Entries)
            {
                if (!from.TryGetValue(entry.transform, out float start)) continue;
                float want = LeanTween.easeOutQuad(start, destinations[entry.transform], t);
                Check(Mathf.Abs(entry.transform.localPosition.x - want) < 2,
                    "actual native easeOutQuad waveform and full duration survive row-plane repair");
                Check(Mathf.Abs(entry.transform.localPosition.z) < .01f,
                    "entry plane remains stable throughout manual board motion and scaling");
            }
            if (elapsed > 2) throw new Exception("slide did not terminate");
        }
        Check(samples >= 3, "the complete slide is observed across real Unity frames");
        Check(Time.unscaledTime - began >= scene.Track.trackReorderDuration - .035f,
            "native duration is not shortened by the changed row or paused Unity clock");
        scene.Track.isAnimating = false; scene.Track.moveXAnimations.Clear(); Time.timeScale = 1;
        yield return null; scene.Tick(); scene.NativeLayout();
        Check(scene.Track.layoutGroup!.enabled && scene.Track.contentSizeFitter!.enabled,
            "layout and fitter return to original native ownership");
        foreach (var entry in scene.Entries)
            Check(Mathf.Abs(entry.transform.localPosition.x - destinations[entry.transform]) < .1f,
                "native actor sibling order and final layout slots remain authoritative");
        scene.PlaneAndPixels(remove ? "removed-settled" : "added-settled");
    }

    private static IEnumerator Cold()
    {
        using var scene = new Scene(4);
        scene.ScheduleNativeBatch();
        var native = ThroughNativeUpdate(scene); while (native.MoveNext()) yield return native.Current;
        var before = scene.Entries.ToDictionary(entry => entry.transform, entry => entry.transform.localPosition);
        scene.Convert();
        var destinations = scene.LayoutOracle();
        // Adoption is complete, but the first real native Update precedes the first surface LateTick.
        yield return null;
        LeanTween.update();
        Check(scene.Entries.Any(entry => Mathf.Abs(entry.transform.localPosition.z) > 2),
            "cold conversion witnesses the actual native world-X write in the new VR frame");
        scene.Tick();
        scene.PlaneAndPixels("cold-conversion");
        foreach (var entry in scene.Entries)
            Check(Mathf.Abs(entry.transform.localPosition.y - before[entry.transform].y) < .1f,
                "cold conversion retains native pre-foreign row Y instead of latching the first leaked pose");
        Check(scene.Surface.Sliding, "cold conversion retains the already-running native reorder");
        float began = Time.unscaledTime;
        int samples = 0;
        while (scene.Surface.Sliding)
        {
            yield return null; scene.Tick(); samples++;
            float t = Mathf.Clamp01((Time.unscaledTime - began) / scene.Track.trackReorderDuration);
            foreach (var entry in scene.Entries)
            {
                float want = LeanTween.easeOutQuad(before[entry.transform].x, destinations[entry.transform], t);
                Check(Mathf.Abs(entry.transform.localPosition.x - want) < 2,
                    "cold adoption keeps native layout destinations and easeOutQuad from captured origins");
            }
            if (Time.unscaledTime - began > 2) throw new Exception("cold slide did not terminate");
        }
        Check(samples >= 3 && Time.unscaledTime - began >= scene.Track.trackReorderDuration - .035f,
            "cold adoption plays the full native duration instead of skipping an already-moving row");
        scene.PlaneAndPixels("cold-completed");
        scene.Track.isAnimating = false; scene.Track.moveXAnimations.Clear();
        yield return null; scene.Tick();
        foreach (var entry in scene.Track.actorsUI) entry.Order = 10 - entry.Order;
        scene.ScheduleNativeBatch();
        native = ThroughNativeUpdate(scene); while (native.MoveNext()) yield return native.Current;
        scene.Tick();
        Check(scene.Surface.Sliding, "lifecycle witness owns an actual in-flight native row plan");
        scene.Surface.Shutdown();
        Check(!scene.Surface.Sliding, "release retires the real slide before native home restoration");
        Check(scene.Track.transform.parent == scene.NativeHome, "native target returns to its exact original home");
        // A fresh lease must not inherit the previous conversion's plan.
        scene.Track.isAnimating = false; scene.Track.moveXAnimations.Clear();
        scene.NativeLayout(); scene.Convert(); yield return null; scene.Tick();
        scene.PlaneAndPixels("cold-reopened");
    }

    private static IEnumerator ThroughNativeUpdate(Scene scene)
    {
        do { yield return null; }
        while (scene.BatchCreatedFrame < 0 || Time.frameCount <= scene.BatchCreatedFrame);
        // This is the exact method called by the original LeanTween.Update. Its
        // native frameRendered guard prevents a second step if Unity already called it.
        LeanTween.update();
        Check(Time.frameCount > scene.BatchCreatedFrame, "native first Update follows the source-bound EOF phase port frame");
    }

    private static IEnumerator Idle(bool abortFirst)
    {
        using var scene = new Scene(4);
        scene.Convert();
        // A complete native/production slide leaves a known clean idle latch.
        scene.Track.isAnimating = true; scene.Tick(); scene.ScheduleNativeBatch();
        var native = ThroughNativeUpdate(scene); while (native.MoveNext()) yield return native.Current;
        scene.Tick();
        if (abortFirst)
        {
            scene.Track.isAnimating = false; scene.Track.moveXAnimations.Clear();
            scene.Add(9);
            yield return null; scene.Tick();
            Check(!scene.Surface.Sliding, "native pool change without a new batch retires the obsolete plan");
            yield return null; Canvas.ForceUpdateCanvases();
        }
        else
        {
            float deadline = Time.unscaledTime + 2;
            while (scene.Surface.Sliding && Time.unscaledTime < deadline)
            { yield return null; scene.Tick(); }
            scene.Track.isAnimating = false; scene.Track.moveXAnimations.Clear();
            yield return null; scene.Tick(); Canvas.ForceUpdateCanvases();
        }
        scene.Track.actorsUI.Reverse();
        foreach (var entry in scene.Track.actorsUI) entry.Order = 10 - entry.Order;
        // Flag and batch start after the previous coordinator pass. Next observation
        // therefore contains both the rising flag and the already-integrated native step.
        scene.ScheduleNativeBatch();
        native = ThroughNativeUpdate(scene); while (native.MoveNext()) yield return native.Current;
        Check(scene.Entries.Any(entry => Mathf.Abs(entry.transform.localPosition.z) > 2),
            "ordinary late-observed native batch supplies a causal out-of-plane witness");
        scene.Tick();
        scene.PlaneAndPixels(abortFirst ? "after-abort" : "idle-rising");
    }
}
