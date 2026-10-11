using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI
{
    internal static partial class LazyTemplateProbe
    {
        internal static IReadOnlyList<Part> FreezeRing645(Transform original)
        {
            var entry = new Entry { Original = original }; Freeze("enchant.holder", entry);
            Entries.Add("enchant.holder", entry); WarmEnhancementBasis("enchant.holder"); return entry.Parts;
        }
        internal static void WarmRing645() => WarmEnhancementBasis("enchant.holder");
    }
}

public static class NativeRingTime645 { public static double Time; }

public static partial class MirrorProgram
{
    private const BindingFlags NativeFields645 = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    private static void NativeSet645(object target, string field, object value) => target.GetType().GetField(field, NativeFields645)!.SetValue(target, value);
    private static T NativeGet645<T>(object target, string field) => (T)target.GetType().GetField(field, NativeFields645)!.GetValue(target)!;
    private static void NativePump645(UIEnchantressEffect effect, LoopAnimator loop, float delta = 1f / 90f)
    {
        NativeGet645<LTDescr>(effect, "rotationAnimation").setUseManualTime(true);
        foreach (var tween in NativeGet645<List<LTDescr>>(loop, "currentAnim")) tween.setUseManualTime(true);
        LeanTween.dtManual = delta;
        typeof(LeanTween).GetField("frameRendered", NativeFields645)!.SetValue(null, -1);
        LeanTween.update();
    }
    private static Transform NativeAt645(Transform root, string path)
    {
        foreach (string token in path.Split('/'))
        {
            if (token.Length == 0) continue;
            int colon = token.LastIndexOf(':'); string name = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token.Substring(0, colon)));
            int occurrence = int.Parse(token.Substring(colon + 1)); Transform? found = null;
            for (int i = 0; i < root.childCount; i++) if (root.GetChild(i).name == name && occurrence-- == 0) { found = root.GetChild(i); break; }
            root = found!;
        }
        return root;
    }
    private static IEnumerator NativeRingShared645(bool partitioned = false, float speed = 1f, bool normalClock = false)
    {
        TownServiceMirror.Shutdown(); LeanTween.reset(); _offeredSequence629 = 645000;
        Time.timeScale = 1f; NativeRingTime645.Time = 1000000000d;
        Transform owner = Go("Actual native ring owner").transform;
        Transform observer = Go("Native offered frame observer").transform;
        observer.SetPositionAndRotation(new Vector3(5, .13f, -.2f), Quaternion.Euler(0, 73, 0));
        observer.localScale = Vector3.one * 1.3f;
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        RectTransform canvas = (RectTransform)Go("Original holder conversion", owner).transform;
        canvas.localScale = new Vector3(.0011f, .0008f, .0012f);
        canvas.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform holder = (RectTransform)NativeRow632(canvas, "");
        holder.gameObject.SetActive(false); holder.localScale = Vector3.one * .52f;
        holder.Find("CardHolder").GetComponent<CanvasGroup>().alpha = 1f;
        holder.Find("GUI_LevelUp_Frame").gameObject.SetActive(false);
        holder.Find("Enhancement Ability Highlight Variant").gameObject.SetActive(false);
        var highlighter = holder.gameObject.AddComponent<GloomhavenVR.WorldUI.UIEnhancementCardHighlighter>();
        var card = Go("Native card", holder.Find("CardHolder")).AddComponent<AbilityCardUI>(); highlighter.Card = card;
        RectTransform print = (RectTransform)Go("FullAbilityCard", card.transform).transform;
        print.sizeDelta = new Vector2(294, 450); card.fullAbilityCard = print;
        RectTransform aura = (RectTransform)holder.Find("Aura"); aura.localRotation = Quaternion.Euler(0, 0, 350);
        RectTransform types = (RectTransform)aura.Find("Types");
        CanvasGroup gate = types.GetComponent<CanvasGroup>();
        RectTransform buy = (RectTransform)types.Find("Buy"); RectTransform sell = (RectTransform)types.Find("Sell");
        Image ink = aura.Find("Highlight").GetComponent<Image>(); ink.GetComponent<CanvasGroup>().alpha = 1f;
        Check(aura.GetComponentsInChildren<Graphic>(true).Length == 3 && aura.GetComponentsInChildren<Transform>(true).Length < 32
            && aura.GetComponent<Graphic>() == null && ink.sprite != null,
            "actual original five-node Aura owns all three native sprites within one native partition");
        var loop = types.gameObject.AddComponent<LoopAnimator>();
        NativeSet645(loop, "effects", new List<AnimationSetting> { new AnimationSetting(types, TweenAction.CANVASGROUP_ALPHA, .6f, 1f, 2f, LeanTweenType.linear) });
        NativeSet645(loop, "autoStart", false); NativeSet645(loop, "ignoreTimeScale", false);
        var effect = aura.gameObject.AddComponent<UIEnchantressEffect>();
        NativeSet645(effect, "enchantressEffect", aura.gameObject); NativeSet645(effect, "rotationTime", 20f);
        NativeSet645(effect, "rotationSpeed", speed); NativeSet645(effect, "idleAnimator", loop);
        NativeSet645(effect, "buyEffect", buy.gameObject); NativeSet645(effect, "sellEffect", sell.gameObject);
        RectTransform physical = (RectTransform)Go("Actual adopted native print", owner).transform;
        physical.sizeDelta = print.sizeDelta; physical.localScale = Vector3.one * .00052f;
        physical.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace; physical.gameObject.AddComponent<CanvasGroup>();
        Image("Original native card ink", physical, Vector2.zero, print.sizeDelta, Color.red);
        holder.gameObject.SetActive(true); effect.ShowModeEffect(true);
        TownServiceEnhancementHandoff.PhysicalCardFace = physical;
        var mask = card.gameObject.AddComponent<TownServiceNativeEnhancementCardMask>(); mask.Mask(); mask.SendMessage("LateUpdate");
        Func<Transform, bool> exclude = node => node == aura;
        if (partitioned)
        {
            for (int i = 0; i < 40; i++) Go("Original partition sibling " + i, holder);
            aura.SetAsLastSibling();
        }
        float expectedRate = speed * 18f;
        NativeSet645(effect, "rotationTime", .000000000000000000000000000001f);
        NativeSet645(effect, "rotationSpeed", float.MaxValue);
        Check(!TownServiceMirror.ReadNativeRingRate(holder, out _, out _), "derived native rate rejects finite-field overflow");
        NativeSet645(effect, "rotationTime", 20f); NativeSet645(effect, "rotationSpeed", speed);
        GameObject bank = Go("Native immutable template provenance"); bank.SetActive(false); LazyTemplateProbe.Open(bank);
        var parts = LazyTemplateProbe.FreezeRing645(holder);
        Check((partitioned ? parts.Count > 1 : parts.Count == 1), "actual immutable holder partitioning follows production bounded topology");
        bool hasRate = false; foreach (var part in parts) if (part.NativeRingRoot != null && part.NativeRingRate == expectedRate) hasRate = true;
        Check(hasRate,
            "actual Freeze retains native serialized rate and ink-root provenance before neutralization");
        Check(parts[0].Original.GetComponentsInChildren<UIEnchantressEffect>(true).Length == 0,
            "actual immutable template bank strips native effect callbacks before registration");
        TownServiceMirror.ForgetTemplates(3, "enchant.holder|");
        LazyTemplateProbe.WarmRing645();
        Check(LazyTemplateProbe.Resolve(3, 1, "enchant.holder|"),
            "actual resolver can re-register retained frozen authored timing after cache invalidation");
        TownServiceMirror.RegisterTemplate(3, 3, physical, address: "face.645|");
        TownServiceMirror.BeginSession(3, 645, owner, owner);
        ushort ringModule = 0; var modules = new List<ushort>();
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i]; ushort id = (ushort)(10 + i); modules.Add(id);
            Transform original = NativeAt645(holder, part.Path);
            var excluded = new HashSet<string>(); foreach (Transform child in part.Excluded) excluded.Add(NativeTemplates.Append("", child));
            TownServiceMirror.RegisterModule(id, 1, original, node => excluded.Contains(NativeTemplates.Append("", node)), "enchant.holder|" + part.Path);
            if (part.NativeRingRoot != null) ringModule = id;
        }
        TownServiceMirror.RegisterModule(600, 3, physical, address: "face.645|"); modules.Add(600);
        foreach (ushort module in modules) TownServiceMirror.SetPriority(module, true);
        TownServiceMirror.RegisterOfferedFrame(holder, physical);
        FastCapture first = OfferedCapture629(); Receive(1, first.Artwork); CompleteOfferedArtwork632(first.Artwork); DeliverMotion(1, first);
        IEnumerator settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
        TownServiceBinding ringCopy = Remote(1, ringModule)!, faceCopy = Remote(1, 600)!;
        Check(ringCopy != null && faceCopy != null, "actual native ring partition and adopted print admitted through production receiver");
        Transform remoteInk = ringCopy.Root.Find("Highlight") ?? ringCopy.Root.Find("Aura/Highlight");
        Check(ringCopy.Root.GetComponentsInChildren<UIEnchantressEffect>(true).Length == 0
            && ringCopy.Root.GetComponentsInChildren<LoopAnimator>(true).Length == 0,
            "observer ring advances without native effect or loop gameplay callbacks");
        effect.Play(); float clock = Time.unscaledTime + (normalClock ? 0f : 1f);
        double nativeTime = 1000000000d, lastNativeTime = Time.timeAsDouble;
        float last = 0; bool sampled = false; int stalled = 0, backward = 0; float maxStep = 0, maxDiameterError = 0;
        var trace = new System.Text.StringBuilder("frame,source,remote,advance,packets\n");
        for (int frame = 1; frame <= (normalClock ? 90 : 540); frame++)
        {
            Time.timeScale = normalClock ? (frame <= 20 ? 1f : frame <= 40 ? 0f : frame <= 60 ? .5f : 2f)
                : (frame >= 210 && frame <= 225 ? 0f : frame >= 300 && frame <= 330 ? .5f : frame >= 390 && frame <= 420 ? 2f : 1f);
            float delta = normalClock ? (float)(Time.timeAsDouble - lastNativeTime) : Time.timeScale / 90f;
            lastNativeTime = Time.timeAsDouble;
            float age = frame / 90f, now = normalClock ? Time.unscaledTime : clock + age;
            nativeTime += delta; NativeRingTime645.Time = nativeTime;
            if (!normalClock) NativePump645(effect, loop, delta);
            // Actual card gaze/fitting continues while its native world-Z tween
            // is fitted into the printed card's plane. The source-native mask,
            // full capture/codec and final print-affinity writer all execute.
            TownServiceNativeEnhancementCardMask.PrepareCurrentPlacement();
            physical.SetPositionAndRotation(new Vector3(.17f, .8f + (normalClock ? 0f : Mathf.Sin(age * 2) * .01f), -.2f), Quaternion.Euler(9f, 70f + (normalClock ? 0f : age * 7f), -3f));
            canvas.localScale = new Vector3(.0011f, .0008f, .0012f);
            if (!normalClock && frame == 120) print.localRotation = Quaternion.Euler(0, 0, 29);
            if (!normalClock && frame == 240) print.localRotation = Quaternion.Euler(0, 0, 90);
            if (!normalClock && frame == 360) print.localRotation = Quaternion.Euler(0, 0, -29);
            mask.SendMessage("LateUpdate");
            int packets = 0;
            if (frame % 6 == 0 && frame % 30 != 0 && !(frame >= 180 && frame <= 204))
            {
                FastCapture capture = OfferedCapture629();
                OfferedReceive629(capture, now); packets = capture.Motion.Count;
            }
            // A full older artwork heartbeat arrives amid newer fast samples.
            // It is really admitted/applied before the final numeric/print writer.
            if (frame == 150 || frame == 300 || frame == 450)
            {
                foreach (byte[] old in first.Artwork)
                {
                    if (!TownServiceCodec.TryRead(old, old.Length, out var artwork) || artwork!.Module == TownServiceFrame.ManifestModule) continue;
                    artwork.Sequence = ++_offeredSequence629; artwork.BaseSequence = 0;
                    Check(TownServiceMirror.Receive(1, TownServiceCodec.Write(artwork), TownServiceCodec.Write(artwork).Length),
                        "older original artwork heartbeat enters real receiver");
                }
                TownServiceMirror.TickRemote(_ => observer);
            }
            OfferedRender629(now);
            var corners = new Vector3[4]; ((RectTransform)remoteInk).GetWorldCorners(corners);
            Vector3 right = corners[3] - corners[0], up = corners[1] - corners[0];
            Vector3 printedRight = faceCopy.Root.InverseTransformDirection(right);
            float phase = Mathf.Atan2(printedRight.y, printedRight.x) * Mathf.Rad2Deg;
            float advance = sampled ? Mathf.DeltaAngle(last, phase) : 0;
            Quaternion nativeRelative = Quaternion.Inverse(physical.rotation) * ink.transform.rotation;
            if (frame > 1)
            {
                if (Mathf.Abs(expectedRate * delta) > .02f && Mathf.Abs(advance) < .02f) stalled++;
                if (advance * Mathf.Sign(expectedRate) < -.02f) backward++;
                maxStep = Mathf.Max(maxStep, Mathf.Abs(advance));
                Check(Vector3.Dot(Vector3.Cross(right, up).normalized, faceCopy.Root.forward) > .999f,
                    "native rotating aura final writer retains the same current physical print plane frame=" + frame
                    + " dot=" + Vector3.Dot(Vector3.Cross(right, up).normalized, faceCopy.Root.forward));
                Check(Mathf.Abs(right.magnitude - up.magnitude) < .0006f,
                    "native ring retains a circular square ink basis across principal-axis changes frame=" + frame
                    + " width=" + right.magnitude + " height=" + up.magnitude
                    + " sourceRotation=" + ink.transform.rotation + " remoteRotation=" + remoteInk.rotation);
                Check(Mathf.Abs(advance - expectedRate * delta) < .001f,
                    "final visible ink advances at its exact authored 20-second native rate on each render expected=" + expectedRate * delta + " actual=" + advance + " frame=" + frame);
                var nativeCorners = new Vector3[4]; ink.rectTransform.GetWorldCorners(nativeCorners);
                float nativeDiameter = Mathf.Sqrt(Vector3.Distance(nativeCorners[0], nativeCorners[1]) * Vector3.Distance(nativeCorners[0], nativeCorners[3])) * observer.localScale.x;
                maxDiameterError = Mathf.Max(maxDiameterError, Mathf.Abs(right.magnitude - nativeDiameter) / nativeDiameter);
                Check(Mathf.Abs(right.magnitude - nativeDiameter) < nativeDiameter * .05f,
                    "observer preserves native ring diameter relative to the current card print frame=" + frame + " owner=" + nativeDiameter + " observer=" + right.magnitude);
                Check(Vector3.Distance(faceCopy.Root.InverseTransformPoint((corners[0] + corners[2]) * .5f),
                    physical.InverseTransformPoint((nativeCorners[0] + nativeCorners[2]) * .5f)) < 1f,
                    "observer rotating ink retains its authored center during native phase correction");
                Check(Mathf.Abs(Vector3.Dot(right.normalized, up.normalized)) < .001f,
                    "native ring ink has no intermediate shear while its physical print turns");
            }
            trace.AppendLine(frame + "," + nativeRelative.eulerAngles.z + "," + phase + "," + advance + "," + packets);
            last = phase; sampled = true;
            // Unity's real render time must advance for the production sampler's
            // per-send clock. The native manual clock advances only in Pump above.
            LeanTween.dtManual = 0;
            yield return null;
        }
        File.WriteAllText(Path.Combine(_output, "native-ring645.csv"), trace.ToString());
        Check(stalled == 0, "final native offered ring advances on every rendered frame; stalled=" + stalled);
        Check(backward == 0, "final native offered ring preserves its signed source phase; backwards=" + backward);
        Check(normalClock || maxStep < .5f, "final native offered ring has no packet-sized jumps; max=" + maxStep);
        Time.timeScale = 1f;
        if (!normalClock)
        {
            // Sample the final authored geometry, then stop only the deterministic
            // fixture clock from advancing it while the receiver settles. This is
            // not a claim of source visibility after a real native Stop call.
            LeanTween.dtManual = 0; yield return null;
            float endpoint = clock + 6.1f;
            OfferedReceive629(OfferedCapture629(), endpoint);
            for (int i = 0; i <= 20; i++) OfferedRender629(endpoint + i / 90f);
            RectTransform remoteAura = (RectTransform)(remoteInk.parent);
            foreach (string path in new[] { "Highlight", "Types/Buy", "Types/Sell" })
            {
                var ownerCorners = new Vector3[4]; var observerCorners = new Vector3[4];
                ((RectTransform)aura.Find(path)).GetWorldCorners(ownerCorners);
                ((RectTransform)remoteAura.Find(path)).GetWorldCorners(observerCorners);
                float authored = Mathf.Sqrt(Vector3.Distance(ownerCorners[0], ownerCorners[1]) * Vector3.Distance(ownerCorners[0], ownerCorners[3])) * observer.localScale.x;
                float observed = Mathf.Sqrt(Vector3.Distance(observerCorners[0], observerCorners[1]) * Vector3.Distance(observerCorners[0], observerCorners[3]));
                Check(Mathf.Abs(authored - observed) < .00006f,
                    "settled observer preserves exact native sprite diameter path=" + path + " owner=" + authored + " observer=" + observed);
            }
        }
        File.WriteAllText(Path.Combine(_output, "native-ring645-summary.txt"),
            "Maximum transient diameter error=" + maxDiameterError + "; max phase advance=" + maxStep + "; stalled=" + stalled + "; reversed=" + backward + "\n");
        LeanTween.dtManual = 0; yield return null;
        effect.Stop(); holder.gameObject.SetActive(false); TownServiceMirror.RegisterOfferedFrame(holder, null);
        FastCapture withdrawn = OfferedCapture629(); OfferedReceive629(withdrawn, clock + 7f); OfferedRender629(clock + 7f);
        Quaternion stopped = remoteInk.localRotation;
        Check(((IDictionary)typeof(TownServiceMirror).GetField("OfferedRemoteMotion", NativeFields645)!.GetValue(null)!).Count == 0,
            "withdrawn original offer retires its cached native presentation clock");
        for (int i = 1; i <= 18; i++)
        {
            NativeRingTime645.Time = nativeTime + i / 90f;
            OfferedRender629(clock + 7f + i / 90f);
            Check(Quaternion.Angle(stopped, remoteInk.localRotation) < .001f,
                "real Stop and hidden-offer withdrawal leave no orphan render clock despite advancing scaled time");
        }
        NativeRingTime645.Time = nativeTime;
        LeanTween.dtManual = 0; yield return null;
        holder.gameObject.SetActive(true); effect.Play(); TownServiceMirror.RegisterOfferedFrame(holder, physical);
        NativePump645(effect, loop); mask.SendMessage("LateUpdate");
        OfferedReceive629(OfferedCapture629(), clock + 8f); OfferedRender629(clock + 8f);
        Check(ringCopy.Root.gameObject.activeInHierarchy, "cached inert binding reopens the offered ring after withdrawal");
        Vector3 previousRight = faceCopy.Root.InverseTransformDirection(remoteInk.TransformVector(Vector3.right));
        for (int i = 1; i <= 18; i++)
        {
            NativeRingTime645.Time = nativeTime + i / 90f;
            OfferedRender629(clock + 8f + i / 90f);
            Vector3 current = faceCopy.Root.InverseTransformDirection(remoteInk.TransformVector(Vector3.right));
            float delta = Mathf.DeltaAngle(Mathf.Atan2(previousRight.y, previousRight.x) * Mathf.Rad2Deg,
                Mathf.Atan2(current.y, current.x) * Mathf.Rad2Deg);
            Check(normalClock || Mathf.Abs(delta - expectedRate / 90f) < .001f, "reopened final native ring advances at authored signed rate"); previousRight = current;
        }
        effect.Stop(); mask.Restore(); TownServiceMirror.Shutdown();
        LazyTemplateProbe.Close(); UnityEngine.Object.DestroyImmediate(bank);
        UnityEngine.Object.DestroyImmediate(owner.gameObject); UnityEngine.Object.DestroyImmediate(observer.gameObject); LeanTween.reset();
    }
}
