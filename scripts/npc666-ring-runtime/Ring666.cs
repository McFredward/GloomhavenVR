using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Rig;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI
{
    internal static partial class LazyTemplateProbe
    {
        internal static IReadOnlyList<Part> FreezeRing666(Transform original)
        {
            var entry = new Entry { Original = original }; Freeze("enchant.holder", entry);
            Entries.Add("enchant.holder", entry); WarmEnhancementBasis("enchant.holder"); return entry.Parts;
        }
        internal static void WarmRing666() => WarmEnhancementBasis("enchant.holder");
    }
}

internal static class RingClock666 { internal static bool Controlled; internal static float Now; internal static float Read => Controlled ? Now : Time.unscaledTime; }

public static class NativeRingTime666 { public static double Time; }

public static partial class MirrorProgram
{
    private const BindingFlags NativeFields666 = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    private static void NativeSet666(object target, string field, object value) => target.GetType().GetField(field, NativeFields666)!.SetValue(target, value);
    private static T NativeGet666<T>(object target, string field) => (T)target.GetType().GetField(field, NativeFields666)!.GetValue(target)!;
    private static void NativePump666(UIEnchantressEffect effect, LoopAnimator loop, float delta = 1f / 90f)
    {
        NativeGet666<LTDescr>(effect, "rotationAnimation").setUseManualTime(true);
        foreach (var tween in NativeGet666<List<LTDescr>>(loop, "currentAnim")) tween.setUseManualTime(true);
        LeanTween.dtManual = delta;
        typeof(LeanTween).GetField("frameRendered", NativeFields666)!.SetValue(null, -1);
        LeanTween.update();
    }
    private static Transform NativeAt666(Transform root, string path)
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
    private static IEnumerator NativeRingShared666(bool partitioned = false, float speed = 1f, bool normalClock = false, bool anisotropic = false, bool irregular = false, bool reflected = false, bool nativeSeat = false)
    {
        TownServiceMirror.Shutdown(); RingClock666.Controlled = false; LeanTween.reset(); _offeredSequence629 = 666000;
        Time.timeScale = 1f; NativeRingTime666.Time = 1000000000d;
        Transform owner = Go("Actual native ring owner").transform;
        Transform observer = Go("Native offered frame observer").transform;
        observer.SetPositionAndRotation(new Vector3(5, .13f, -.2f), Quaternion.Euler(0, 73, 0));
        observer.localScale = reflected ? new Vector3(-1.3f,.85f,1.1f) : anisotropic ? new Vector3(1.3f, .85f, 1.1f) : Vector3.one * 1.3f;
        owner.localScale = Vector3.one;
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        Transform seat=Go("Actual native Place offering seat",owner).transform;
        Transform palm=Go("Actual source offering palm",owner).transform;
        palm.localPosition=new Vector3(.17f,.63f,-.2f);
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
        NativeSet666(loop, "effects", new List<AnimationSetting> { new AnimationSetting(types, TweenAction.CANVASGROUP_ALPHA, .6f, 1f, 2f, LeanTweenType.linear) });
        NativeSet666(loop, "autoStart", false); NativeSet666(loop, "ignoreTimeScale", false);
        var effect = aura.gameObject.AddComponent<UIEnchantressEffect>();
        NativeSet666(effect, "enchantressEffect", aura.gameObject); NativeSet666(effect, "rotationTime", 20f);
        NativeSet666(effect, "rotationSpeed", speed); NativeSet666(effect, "idleAnimator", loop);
        NativeSet666(effect, "buyEffect", buy.gameObject); NativeSet666(effect, "sellEffect", sell.gameObject);
        RectTransform physical = (RectTransform)Go("Actual adopted native print", owner).transform;
        physical.sizeDelta = print.sizeDelta; physical.localScale = Vector3.one * .00052f;
        physical.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace; physical.gameObject.AddComponent<CanvasGroup>();
        Image("Original native card ink", physical, Vector2.zero, print.sizeDelta, Color.red);
        Transform physicalCard = Go("Actual native body owner",owner).transform;
        VRCard actual = physicalCard.gameObject.AddComponent<VRCard>(); actual.FixtureBacking(new Vector2(.15288f,.234f));
        Transform physicalBody = physicalCard.Find("Visual/Backing");
        if(nativeSeat)
        {
            physicalCard.SetParent(seat,false);
            physical.SetParent(physicalCard,false);physical.localPosition=new Vector3(0f,0f,-.0012f);
            VRRigDriver.HeadCamera!.transform.position=palm.position-Vector3.forward;
            TownServiceOfferingPose.Place(seat,palm,owner,0f);
        }
        TownServiceMirror.RegisterMotionOffering(physical,true);
        TownServiceMirror.RegisterMotionOffering(physicalBody,true);
        TownServiceMirror.RegisterOfferedPhysical(physicalBody,physical);
        TownServiceMirror.RegisterTemplate(3,4,physicalBody,address:TownServiceAbilityBody.Key(actual) + "|");
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
        NativeSet666(effect, "rotationTime", .000000000000000000000000000001f);
        NativeSet666(effect, "rotationSpeed", float.MaxValue);
        Check(!TownServiceMirror.ReadNativeRingRate(holder, out _, out _), "derived native rate rejects finite-field overflow");
        NativeSet666(effect, "rotationTime", 20f); NativeSet666(effect, "rotationSpeed", speed);
        GameObject bank = Go("Native immutable template provenance"); bank.SetActive(false); LazyTemplateProbe.Open(bank);
        var parts = LazyTemplateProbe.FreezeRing666(holder);
        Check((partitioned ? parts.Count > 1 : parts.Count == 1), "actual immutable holder partitioning follows production bounded topology");
        bool hasRate = false; foreach (var part in parts) if (part.NativeRingRoot != null && part.NativeRingRate == expectedRate) hasRate = true;
        Check(hasRate,
            "actual Freeze retains native serialized rate and ink-root provenance before neutralization");
        Check(parts[0].Original.GetComponentsInChildren<UIEnchantressEffect>(true).Length == 0,
            "actual immutable template bank strips native effect callbacks before registration");
        TownServiceMirror.ForgetTemplates(3, "enchant.holder|");
        LazyTemplateProbe.WarmRing666();
        Check(LazyTemplateProbe.Resolve(3, 1, "enchant.holder|"),
            "actual resolver can re-register retained frozen authored timing after cache invalidation");
        TownServiceMirror.RegisterTemplate(3, 3, physical, address: "face.666|");
        TownServiceMirror.BeginSession(3, 666, owner, owner);
        ushort ringModule = 0; var modules = new List<ushort>();
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i]; ushort id = (ushort)(10 + i); modules.Add(id);
            Transform original = NativeAt666(holder, part.Path);
            var excluded = new HashSet<string>(); foreach (Transform child in part.Excluded) excluded.Add(NativeTemplates.Append("", child));
            TownServiceMirror.RegisterModule(id, 1, original, node => excluded.Contains(NativeTemplates.Append("", node)), "enchant.holder|" + part.Path);
            if (part.NativeRingRoot != null) ringModule = id;
        }
        TownServiceMirror.RegisterModule(600, 3, physical, address: "face.666|"); modules.Add(600);
        TownServiceMirror.RegisterModule(601,4,physicalBody,address:TownServiceAbilityBody.Key(actual) + "|"); modules.Add(601);
        foreach (ushort module in modules) TownServiceMirror.SetPriority(module, true);
        TownServiceMirror.RegisterOfferedFrame(holder, physical);
        FastCapture first = OfferedCapture629(); Receive(1, first.Artwork); CompleteOfferedArtwork632(first.Artwork); DeliverMotion(1, first);
        IEnumerator settle = FastSettle(observer, .14f); while (settle.MoveNext()) yield return settle.Current;
        TownServiceBinding ringCopy = Remote(1, ringModule)!, faceCopy = Remote(1, 600)!, bodyCopy = Remote(1,601)!;
        if(nativeSeat)
        {
            FieldInfo? hover=typeof(TownServiceFrame).GetField("OfferedHover",NativeFields666);
            Check(hover!=null,"native seat composition requires actual117 production DTO, never an inactive fake boundary");
            object? printHover=null,bodyHover=null;
            foreach(byte[] wire in first.Artwork)
            {
                Check(TownServiceCodec.TryRead(wire,wire.Length,out TownServiceFrame? frame),"actual native seat original117 decodes");
                if(frame!.Module==600) printHover=hover!.GetValue(frame);
                if(frame.Module==601) bodyHover=hover!.GetValue(frame);
            }
            Check(printHover!=null && bodyHover!=null,"real Place lineage puts active117 on both native physical parts");
            FieldInfo epoch=printHover!.GetType().GetField("Epoch",NativeFields666)!;
            Check(epoch.GetValue(printHover)!.Equals(epoch.GetValue(bodyHover)),"one real native source card supplies a shared hover preparation epoch");
        }
        bool observedCanvas = false, observedRingBinding = false;
        foreach(byte[] bytes in first.Motion)
        {
            Check(TownServiceMotionCodec.TryRead(bytes,bytes.Length,out TownServiceMotionPacket? initialPacket),"native first offer decodes actual107/109");
            foreach(TownServiceMotionEntry entry in initialPacket!.Entries) if(entry.Kind == 9 && entry.Module == ringModule)
            {
                observedCanvas |= entry.HasCanvasFrame;
                observedRingBinding |= Array.IndexOf(ringCopy.Bindings,entry.Binding) >= 0;
            }
        }
        File.WriteAllText(Path.Combine(_output,"native-root-binding666.txt"),"parts="+parts.Count+";ringModule="+ringModule+";canvas="+observedCanvas+";binding="+observedRingBinding+"\n");
        Check(observedCanvas == !partitioned && observedRingBinding,"actual published native ring partition carries exact binding and its correct enclosing109 CanvasPose or real native parent");
        Check(ringCopy != null && faceCopy != null, "actual native ring partition and adopted print admitted through production receiver");
        Transform remoteInk = ringCopy.Root.Find("Highlight") ?? ringCopy.Root.Find("Aura/Highlight");
        Check(ringCopy.Root.GetComponentsInChildren<UIEnchantressEffect>(true).Length == 0
            && ringCopy.Root.GetComponentsInChildren<LoopAnimator>(true).Length == 0,
            "observer ring advances without native effect or loop gameplay callbacks");
        effect.Play(); float clock = Time.unscaledTime + (normalClock ? 0f : 1f);
        double nativeTime = 1000000000d, lastNativeTime = Time.timeAsDouble;
        float last = 0; bool sampled = false; int stalled = 0, backward = 0; float maxStep = 0, maxDiameterError = 0;
        string csv = Path.Combine(_output, "native-ring666.csv");
        File.WriteAllText(csv, "frame,source,remote,advance,packets,centerError,planeError,yaw\n");
        RingClock666.Controlled = true;
        string[] inkPaths = { "Highlight", "Types/Buy", "Types/Sell" };
        var previousInk = new Dictionary<string, Vector3>();
        Quaternion previousPlane = Quaternion.identity;
        var transit = new List<(int Due,byte[] Wire)>(); int motionEvents = 0; int lost = 0, delayed = 0;
        for (int frame = 1; frame <= (normalClock ? 90 : 540); frame++)
        {
            Time.timeScale = normalClock ? (frame <= 20 ? 1f : frame <= 40 ? 0f : frame <= 60 ? .5f : 2f)
                : (frame >= 210 && frame <= 225 ? 0f : frame >= 300 && frame <= 330 ? .5f : frame >= 390 && frame <= 420 ? 2f : 1f);
            float delta = normalClock ? (float)(Time.timeAsDouble - lastNativeTime) : Time.timeScale / 90f;
            lastNativeTime = Time.timeAsDouble;
            float age = frame / 90f, now = normalClock ? Time.unscaledTime : clock + age;
            nativeTime += delta; NativeRingTime666.Time = nativeTime; RingClock666.Now = now;
            if (!normalClock) NativePump666(effect, loop, delta);
            // Actual card gaze/fitting continues while its native world-Z tween
            // is fitted into the printed card's plane. The source-native mask,
            // full capture/codec and final print-affinity writer all execute.
            TownServiceNativeEnhancementCardMask.PrepareCurrentPlacement();
            if(nativeSeat)
            {
                float yaw=age<=2f?age*36f:72f-(age-2f)*54f;
                VRRigDriver.HeadCamera!.transform.position=palm.position-Quaternion.Euler(0f,yaw,0f)*Vector3.forward;
                TownServiceOfferingPose.Place(seat,palm,owner,age);
                Check(Mathf.Abs(seat.position.y-palm.position.y-.17f-.006f*Mathf.Sin(age*1.8f))<.000001f,
                    "native Place retains original1.8rad/s hover amplitude and waveform while its actual ring faces owner head");
            }
            else physical.SetPositionAndRotation(new Vector3(.17f, .8f + (normalClock ? 0f : Mathf.Sin(age * 2) * .01f), -.2f), Quaternion.Euler(9f, 70f + (normalClock ? 0f : (age <= 2f ? age * 36f : 72f - (age - 2f) * 54f)), -3f));
            canvas.localScale = new Vector3(.0011f, .0008f, .0012f);
            if (!normalClock && frame == 120) print.localRotation = Quaternion.Euler(0, 0, 29);
            if (!normalClock && frame == 240) print.localRotation = Quaternion.Euler(0, 0, 90);
            if (!normalClock && frame == 360) print.localRotation = Quaternion.Euler(0, 0, -29);
            if(!nativeSeat) physicalCard.SetPositionAndRotation(physical.position,physical.rotation);
            mask.SendMessage("LateUpdate");
            int packets = 0;
            if (frame % 90 == 0) effect.ShowModeEffect(frame % 180 == 0);
            if (frame % 6 == 3)
            {
                ink.color = frame % 12 < 6 ? Color.cyan : Color.white;
                FastCapture header = CaptureFast(); Receive(1, header.Artwork);
                QueueRingMotion666(header,frame,irregular,transit,ref motionEvents,ref lost,ref delayed);
            }
            if (frame % 6 == 0 && frame % 30 != 0 && !(frame >= 180 && frame <= 204))
            {
                FastCapture capture = OfferedCapture629();
                Receive(1, capture.Artwork); packets = capture.Motion.Count;
                QueueRingMotion666(capture,frame,irregular,transit,ref motionEvents,ref lost,ref delayed);
            }
            for(int pending=0;pending<transit.Count;)
                if(transit[pending].Due<=frame)
                { var arriving=new FastCapture(); arriving.Motion.Add(transit[pending].Wire); transit.RemoveAt(pending); DeliverMotion(1,arriving); }
                else pending++;
            TownServiceMirror.TickRemote(_ => observer);
            CheckRingBody666(physical,physicalBody,faceCopy.Root,bodyCopy.Root);
            var corners = new Vector3[4]; ((RectTransform)remoteInk).GetWorldCorners(corners);
            Vector3 right = corners[3] - corners[0], up = corners[1] - corners[0];
            Vector3[] basisCorners = new Vector3[4]; ((RectTransform)faceCopy.Root).GetWorldCorners(basisCorners);
            Vector3 basisX = (basisCorners[3] - basisCorners[0]).normalized;
            Vector3 basisZ = Vector3.Cross(basisX, (basisCorners[1] - basisCorners[0]).normalized).normalized;
            Vector3 basisY = Vector3.Cross(basisZ,basisX).normalized;
            Vector3 printedRight = new Vector3(Vector3.Dot(right,basisX),Vector3.Dot(right,basisY),Vector3.Dot(right,basisZ));
            float phase = Mathf.Atan2(printedRight.y, printedRight.x) * Mathf.Rad2Deg;
            float advance = sampled ? Mathf.DeltaAngle(last, phase) : 0;
            Quaternion nativeRelative = Quaternion.Inverse(physical.rotation) * ink.transform.rotation;
            var printedCorners = new Vector3[4]; ((RectTransform)faceCopy.Root).GetWorldCorners(printedCorners);
            Vector3 printNormal = Vector3.Cross(printedCorners[3] - printedCorners[0], printedCorners[1] - printedCorners[0]).normalized;
            var actualCorners = new Vector3[4]; ink.rectTransform.GetWorldCorners(actualCorners);
            Vector3 actualCenter = physical.InverseTransformPoint((actualCorners[0] + actualCorners[2]) * .5f);
            float centerError = Vector3.Distance(faceCopy.Root.TransformPoint(actualCenter), (corners[0] + corners[2]) * .5f);
            float planeError = Vector3.Distance(printNormal, Vector3.Cross(right, up).normalized);
            File.AppendAllText(csv, frame + "," + nativeRelative.eulerAngles.z + "," + phase + "," + advance + "," + packets
                + "," + centerError + "," + planeError + "," + faceCopy.Root.eulerAngles.y + "\n");
            if(frame>20)
            {
                foreach (string path in inkPaths)
                {
                    RectTransform native = (RectTransform)aura.Find(path);
                    RectTransform remote = (RectTransform)(remoteInk.parent!.Find(path));
                    var allCorners = new Vector3[4]; remote.GetWorldCorners(allCorners);
                    Vector3 actualNormal = Vector3.Cross(allCorners[3] - allCorners[0], allCorners[1] - allCorners[0]).normalized;
                    File.AppendAllText(csv, "ink," + frame + "," + path + "," + Vector3.Distance(actualNormal, printNormal) + "\n");
                    Check(Vector3.Distance(actualNormal, printNormal) < .00001f,
                        "each original native cyan sprite follows the actual rendered card plane path=" + path + " frame=" + frame + " error=" + Vector3.Distance(actualNormal, printNormal));
                }
            }
            Quaternion renderedPlane = Quaternion.LookRotation(basisZ,basisY);
            foreach (string path in inkPaths)
            {
                RectTransform drawing = (RectTransform)remoteInk.parent!.Find(path);
                var points = new Vector3[4]; drawing.GetWorldCorners(points);
                Vector3 currentRay = (points[3] - points[0]).normalized;
                if (drawing.gameObject.activeInHierarchy && frame > 20 && previousInk.TryGetValue(path,out Vector3 preceding))
                {
                    Vector3 inherited = renderedPlane * Quaternion.Inverse(previousPlane) * preceding;
                    Vector3 expected = Quaternion.AngleAxis(expectedRate * delta,basisZ) * inherited;
                    float stepError = Vector3.Distance(expected,currentRay);
                    File.AppendAllText(csv,"step," + frame + "," + path + "," + stepError + "\n");
                    Check(stepError < .00002f,
                        "all visible native cyan sprites inherit final rendered card yaw and exact native circular rate each render path=" + path
                        + " frame=" + frame + " error=" + stepError);
                }
                if(drawing.gameObject.activeInHierarchy) previousInk[path] = currentRay; else previousInk.Remove(path);
            }
            previousPlane = renderedPlane;
            if (frame > 20)
            {
                Check(centerError < .00003f, "original native cyan points stay centered on the rendered physical print across sparse yaw and UI headers frame=" + frame + " gap=" + centerError);
                Check(planeError < .00001f, "original native cyan points retain the actual rendered print-corner plane frame=" + frame + " error=" + planeError);
                if (Mathf.Abs(expectedRate * delta) > .02f && Mathf.Abs(advance) < .02f) stalled++;
                if (advance * Mathf.Sign(expectedRate) < -.02f) backward++;
                maxStep = Mathf.Max(maxStep, Mathf.Abs(advance));
                Check(Vector3.Dot(Vector3.Cross(right, up).normalized, printNormal) > .999f,
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
                if(!anisotropic && !reflected)
                {
                    maxDiameterError = Mathf.Max(maxDiameterError, Mathf.Abs(right.magnitude - nativeDiameter) / nativeDiameter);
                    Check(Mathf.Abs(right.magnitude - nativeDiameter) < nativeDiameter * .05f,
                        "observer preserves native ring diameter relative to the current card print frame=" + frame + " owner=" + nativeDiameter + " observer=" + right.magnitude);
                }
                Check(Vector3.Distance(faceCopy.Root.InverseTransformPoint((corners[0] + corners[2]) * .5f),
                    physical.InverseTransformPoint((nativeCorners[0] + nativeCorners[2]) * .5f)) < 1f,
                    "observer rotating ink retains its authored center during native phase correction");
                Check(Mathf.Abs(Vector3.Dot(right.normalized, up.normalized)) < .001f,
                    "native ring ink has no intermediate shear while its physical print turns");
            }
            if(frame == 60 || frame == 150 || frame == 300 || frame == 470)
            {
                foreach(string path in inkPaths)
                {
                    RectTransform native=(RectTransform)aura.Find(path), copied=(RectTransform)remoteInk.parent!.Find(path);
                    if(!native.gameObject.activeInHierarchy || !copied.gameObject.activeInHierarchy) continue;
                    string label="frame"+frame+"-"+path.Replace('/','-');
                    Color32[] ownerPixels=RingPixels666(native,8,label+"-native");
                    Color32[] remotePixels=RingPixels666(copied,9,label+"-observer");
                    int ownerPeak=PeakRing666(ownerPixels),remotePeak=PeakRing666(remotePixels),different=0,lit=0;
                    for(int pixel=0;pixel<ownerPixels.Length;pixel++)
                    {
                        bool nativeLit=Math.Max(ownerPixels[pixel].r,Math.Max(ownerPixels[pixel].g,ownerPixels[pixel].b))>ownerPeak/8;
                        bool remoteLit=Math.Max(remotePixels[pixel].r,Math.Max(remotePixels[pixel].g,remotePixels[pixel].b))>remotePeak/8;
                        if(nativeLit) lit++;
                        if(nativeLit!=remoteLit) different++;
                    }
                    File.AppendAllText(Path.Combine(_output,"native-ring666-pixels.csv"),label+","+lit+","+different+","+ownerPeak+","+remotePeak+"\n");
                    Check(ownerPeak>20 && remotePeak>20 && lit>1500 && different<80,
                        "all actual visible native cyan sprites retain original GPU silhouettes after independently verified physical plane/yaw label="+label+" native="+lit+" different="+different);
                }
            }
            last = phase; sampled = true;
            // Unity's real render time must advance for the production sampler's
            // per-send clock. The native manual clock advances only in Pump above.
            LeanTween.dtManual = 0;
            yield return null;
        }

        Check(!irregular || lost>0 && delayed>0,"actual109/root transport exercises loss and delayed reorder");
        Check(stalled == 0, "final native offered ring advances on every rendered frame; stalled=" + stalled);
        Check(backward == 0, "final native offered ring preserves its signed source phase; backwards=" + backward);
        Check(normalClock || maxStep < .5f, "final native offered ring has no packet-sized jumps; max=" + maxStep);
        Time.timeScale = 1f;
        if (!normalClock)
        {
            foreach(var pending in transit) { var late=new FastCapture(); late.Motion.Add(pending.Wire); DeliverMotion(1,late); }
            transit.Clear();
            // Sample the final authored geometry, then stop only the deterministic
            // fixture clock from advancing it while the receiver settles. This is
            // not a claim of source visibility after a real native Stop call.
            LeanTween.dtManual = 0; yield return null;
            float endpoint = clock + 6.1f; RingClock666.Now = endpoint;
            FastCapture lastGeometry = OfferedCapture629(); Receive(1,lastGeometry.Artwork); DeliverMotion(1,lastGeometry);
            for (int i = 0; i <= 20; i++) OfferedRender629(endpoint + i / 90f);
            RectTransform remoteAura = (RectTransform)(remoteInk.parent);
            foreach (string path in new[] { "Highlight", "Types/Buy", "Types/Sell" })
            {
                var ownerCorners = new Vector3[4]; var observerCorners = new Vector3[4];
                ((RectTransform)aura.Find(path)).GetWorldCorners(ownerCorners);
                ((RectTransform)remoteAura.Find(path)).GetWorldCorners(observerCorners);
                float authored = Mathf.Sqrt(Vector3.Distance(ownerCorners[0], ownerCorners[1]) * Vector3.Distance(ownerCorners[0], ownerCorners[3])) * observer.localScale.x;
                float observed = Mathf.Sqrt(Vector3.Distance(observerCorners[0], observerCorners[1]) * Vector3.Distance(observerCorners[0], observerCorners[3]));
                if(!anisotropic && !reflected) Check(Mathf.Abs(authored - observed) < .00006f,
                    "settled observer preserves exact native sprite diameter path=" + path + " owner=" + authored + " observer=" + observed);
            }
        }
        File.WriteAllText(Path.Combine(_output, "native-ring666-summary.txt"),
            "Maximum transient uniform-room diameter error=" + (!anisotropic && !reflected ? maxDiameterError.ToString() : "not measured by uniform-room scale formula")
            + "; max phase advance=" + maxStep + "; stalled=" + stalled + "; reversed=" + backward + "\n");
        LeanTween.dtManual = 0; yield return null;
        effect.Stop(); holder.gameObject.SetActive(false); TownServiceMirror.RegisterOfferedFrame(holder, null);
        RingClock666.Now = clock + 7f; FastCapture withdrawn = OfferedCapture629(); Receive(1, withdrawn.Artwork); DeliverMotion(1, withdrawn); TownServiceMirror.TickRemote(_ => observer);
        Quaternion stopped = remoteInk.localRotation;
        Check(((IDictionary)typeof(TownServiceMirror).GetField("OfferedRemoteMotion", NativeFields666)!.GetValue(null)!).Count == 0,
            "withdrawn original offer retires its cached native presentation clock");
        for (int i = 1; i <= 18; i++)
        {
            NativeRingTime666.Time = nativeTime + i / 90f; RingClock666.Now = clock + 7f + i / 90f;
            TownServiceMirror.TickRemote(_ => observer);
            Check(Quaternion.Angle(stopped, remoteInk.localRotation) < .001f,
                "real Stop and hidden-offer withdrawal leave no orphan render clock despite advancing scaled time");
        }
        NativeRingTime666.Time = nativeTime;
        LeanTween.dtManual = 0; yield return null;
        holder.gameObject.SetActive(true); effect.Play(); TownServiceMirror.RegisterOfferedFrame(holder, physical);
        NativePump666(effect, loop); mask.SendMessage("LateUpdate");
        RingClock666.Now = clock + 8f; FastCapture reopen = OfferedCapture629(); Receive(1,reopen.Artwork); DeliverMotion(1,reopen); TownServiceMirror.TickRemote(_ => observer);
        Check(ringCopy.Root.gameObject.activeInHierarchy, "cached inert binding reopens the offered ring after withdrawal");
        Vector3 previousRight = ReopenedRingRay666(faceCopy.Root,remoteInk);
        for (int i = 1; i <= 18; i++)
        {
            NativeRingTime666.Time = nativeTime + i / 90f; RingClock666.Now = clock + 8f + i / 90f;
            TownServiceMirror.TickRemote(_ => observer);
            Vector3 current = ReopenedRingRay666(faceCopy.Root,remoteInk);
            float delta = Mathf.DeltaAngle(Mathf.Atan2(previousRight.y, previousRight.x) * Mathf.Rad2Deg,
                Mathf.Atan2(current.y, current.x) * Mathf.Rad2Deg);
            Check(normalClock || Mathf.Abs(delta - expectedRate / 90f) < .001f, "reopened final native ring advances at authored signed rate"); previousRight = current;
        }
        effect.Stop(); mask.Restore(); TownServiceMirror.Shutdown(); RingClock666.Controlled = false;
        LazyTemplateProbe.Close(); UnityEngine.Object.DestroyImmediate(bank);
        UnityEngine.Object.DestroyImmediate(owner.gameObject); UnityEngine.Object.DestroyImmediate(observer.gameObject); LeanTween.reset();
    }
    private static void QueueRingMotion666(FastCapture capture,int frame,bool irregular,List<(int Due,byte[] Wire)> transit,
        ref int events,ref int lost,ref int delayed)
    {
        foreach(byte[] bytes in capture.Motion)
        {
            int index=events++;
            if(irregular && index==4) { lost++; continue; }
            int lag=!irregular?0:index==7?10:index%4==1?2:index%4==3?3:0;
            if(lag>0) delayed++;
            transit.Add((frame+lag,bytes));
        }
    }
    private static void CheckRingBody666(RectTransform originalPrint,Transform originalBody,Transform copiedPrint,Transform copiedBody)
    {
        Mesh mesh=originalBody.GetComponent<MeshFilter>().sharedMesh;
        Check(ReferenceEquals(mesh,copiedBody.GetComponent<MeshFilter>().sharedMesh),"ring correction retains actual native physical backing mesh");
        foreach(Vector3 vertex in mesh.vertices)
        {
            Vector3 expected=copiedPrint.TransformPoint(originalPrint.InverseTransformPoint(originalBody.TransformPoint(vertex)));
            Check(Vector3.Distance(expected,copiedBody.TransformPoint(vertex))<.00004f,
                "native body/front geometry stays coherent on every rendered ring/yaw frame");
        }
    }

    private static int PeakRing666(Color32[] pixels)
    {
        int maximum=0;
        foreach(Color32 pixel in pixels) maximum=Math.Max(maximum,Math.Max(pixel.r,Math.Max(pixel.g,pixel.b)));
        return maximum;
    }
    private static Color32[] RingPixels666(RectTransform ink,int layer,string name)
    {
        foreach(GameObject original in Objects) if(original!=null) Layer(original.transform,30);
        Layer(ink,layer);
        foreach(Canvas canvas in ink.GetComponentsInParent<Canvas>(true)) { canvas.gameObject.layer=layer; canvas.worldCamera=_camera; }
        _camera.cullingMask=1<<layer;
        _camera.orthographicSize=ink.rect.height*.58f;
        Matrix4x4 localView=Matrix4x4.TRS(new Vector3(0f,0f,-10f),Quaternion.identity,Vector3.one);
        // Normalize only the measurement camera, after world plane and ray laws
        // have passed. Never relocate or rotate an original or mirrored widget.
        _camera.worldToCameraMatrix=Matrix4x4.Scale(new Vector3(1f,1f,-1f))*localView.inverse*ink.worldToLocalMatrix;
        Color previousBackground=_camera.backgroundColor; _camera.backgroundColor=Color.black;
        var target=new RenderTexture(192,192,24,RenderTextureFormat.ARGB32) { antiAliasing=1 };
        var pixels=new Texture2D(192,192,TextureFormat.RGBA32,false);
        bool culling=GL.invertCulling;
        try
        {
            GL.invertCulling=culling^(ink.localToWorldMatrix.determinant<0f);
            Canvas.ForceUpdateCanvases(); _camera.targetTexture=target; _camera.Render(); RenderTexture.active=target;
            pixels.ReadPixels(new Rect(0f,0f,192f,192f),0,0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(_output,name+".png"),pixels.EncodeToPNG());
            return pixels.GetPixels32();
        }
        finally
        {
            RenderTexture.active=null;_camera.targetTexture=null;_camera.backgroundColor=previousBackground;
            GL.invertCulling=culling;_camera.ResetWorldToCameraMatrix();
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);
        }
    }

    private static Vector3 ReopenedRingRay666(Transform print,Transform ink)
    {
        var corners=new Vector3[4];((RectTransform)print).GetWorldCorners(corners);
        Vector3 right=(corners[3]-corners[0]).normalized, normal=Vector3.Cross(right,(corners[1]-corners[0]).normalized).normalized;
        Vector3 up=Vector3.Cross(normal,right).normalized, ray=ink.TransformVector(Vector3.right).normalized;
        return new Vector3(Vector3.Dot(ray,right),Vector3.Dot(ray,up),Vector3.Dot(ray,normal));
    }

}
