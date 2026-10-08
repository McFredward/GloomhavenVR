using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private static TownServiceFrame HoverFrame645(TownServiceBinding source, ulong sequence, float time) => new()
    {
        Service = 3, Session = 645, Sequence = sequence, SampleTime = time, Module = 11,
        Template = 1, TemplateAddress = "enchant.row|", Structure = source.Structure,
        Visible = true, Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f },
        Nodes = TownServiceDelta.Retain(new TownServiceFrame { Nodes = source.Read(TownServiceMirror.Assets) }).Nodes
    };

    private static string Relative645(Transform root, Transform node)
    { string path = ""; while (node != root) { path = node.name + (path.Length == 0 ? "" : "/" + path); node = node.parent; } return path; }

    private static List<byte> ComposedKinds645(TownServiceBinding binding)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var frames = (IDictionary)typeof(TownServiceMirror).GetField("MotionRemoteFrames", PrivateStatic)!.GetValue(null)!;
        foreach (DictionaryEntry pair in frames)
        {
            if (!ReferenceEquals(pair.Key.GetType().GetField("Binding", flags)!.GetValue(pair.Key), binding)) continue;
            var slots = (IList)pair.Value!.GetType().GetField("Slots", flags)!.GetValue(pair.Value)!;
            var kinds = new List<byte>();
            foreach (object slot in slots)
            {
                var entry = (TownServiceMotionEntry)slot.GetType().GetField("Entry", flags)!.GetValue(slot)!;
                kinds.Add(entry.Kind);
            }
            return kinds;
        }
        throw new InvalidOperationException("Actual native enhancement row has no composed numeric frame.");
    }

    private static Color32[] RenderHover645(Transform surface, Vector3 center, Quaternion facing, int layer, string name)
    {
        // Both cameras use the same authored viewport frame, never recenter on
        // the drifting observer row: its incorrect Y must remain visible.
        foreach (GameObject fixture in Objects) if (fixture != null) Layer(fixture.transform, 30);
        Layer(surface, layer);
        foreach (Canvas canvas in surface.GetComponentsInChildren<Canvas>(true)) canvas.worldCamera = _camera;
        _camera.cullingMask = 1 << layer;
        _camera.transform.SetPositionAndRotation(center - facing * Vector3.forward * 10f, facing);
        _camera.orthographicSize = .38f;
        Canvas.ForceUpdateCanvases();
        var target = new RenderTexture(768, 512, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var image = new Texture2D(768, 512, TextureFormat.RGBA32, false);
        try
        {
            _camera.targetTexture = target; _camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new UnityEngine.Rect(0, 0, 768, 512), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(_output, name + ".png"), image.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(_output, name + ".jpg"), image.EncodeToJPG(90));
            return image.GetPixels32();
        }
        finally
        {
            RenderTexture.active = null; _camera.targetTexture = null;
            Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        }
    }

    private static void LayoutMatrix645()
    {
        // Native root layout and world-pose headers are distinct contracts.
        // Exercise several authored anchors/pivots, with a header present and
        // absent, plus the existing detached enclosing-canvas caller.
        var minimum = new[] { new Vector2(.5f, 1f), Vector2.zero, Vector2.zero, new Vector2(.5f, .5f) };
        var maximum = new[] { new Vector2(.5f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(.5f, .5f) };
        var pivots = new[] { new Vector2(.5f, 1f), new Vector2(.25f, .7f), new Vector2(.8f, 0f), new Vector2(.5f, .5f) };
        for (int layout = 0; layout < minimum.Length; layout++)
        {
            var owner = Rect("Native layout matrix owner", null!, Vector2.zero, new Vector2(1024f, 800f));
            var root = (RectTransform)NativeRow632(owner, "Layout " + layout);
            root.anchorMin = minimum[layout]; root.anchorMax = maximum[layout]; root.pivot = pivots[layout];
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 512f);
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 70f);
            root.localPosition = new Vector3(37f, -143f, 0f);
            var host = Rect("Native layout matrix observer", null!, Vector2.zero, owner.sizeDelta);
            var clone = Object.Instantiate(root.gameObject, host, false);
            clone.SetActive(false); TownServiceNeutralize.Apply(clone); clone.SetActive(true);
            using var source = new TownServiceBinding(root);
            using var observer = new TownServiceBinding(clone.transform);
            var observed = (RectTransform)observer.Root;
            TownServiceFrame original = HoverFrame645(source, 1, 0f);
            observer.Apply(original, TownServiceMirror.Assets);
            observer.ApplyRootLayout(original, detached: false);
            observed.localPosition = root.localPosition; // Genuine separate header pose.
            var hover = root.GetComponentsInChildren<RectTransform>(true).First(n => n.name == "Content");
            hover.anchoredPosition3D += Vector3.right * 14f;
            TownServiceFrame changed = HoverFrame645(source, 2, .1f);
            observer.Apply(changed, TownServiceMirror.Assets); // Child-only update has no header pose.
            Check(observed.localPosition.Equals(root.localPosition), "native root pose survives a child-only hover without a repair header");
            Check(observed.anchorMin.Equals(root.anchorMin) && observed.anchorMax.Equals(root.anchorMax)
                && observed.pivot.Equals(root.pivot), "parented root keeps every original anchor and pivot variant");
            Check(Vector2.Distance(observed.rect.size, root.rect.size) < .001f, "parented native root keeps its original extent");
            for (int header = 0; header < 2; header++)
            {
                observer.Apply(changed, TownServiceMirror.Assets);
                if (header == 1)
                {
                    observer.ApplyRootLayout(changed, detached: false);
                    observed.localPosition = root.localPosition;
                }
                Check(observed.localPosition.Equals(root.localPosition), "hover geometry is identical with or without a new header");
            }
            observer.ApplyRootLayout(changed, detached: true);
            Check(observed.localPosition.Equals(root.localPosition), "detached root layout cannot translate its independently authored header pose");
            Check(observed.anchorMin.Equals(root.pivot) && observed.anchorMax.Equals(root.pivot)
                && Vector2.Distance(observed.sizeDelta, root.rect.size) < .001f,
                "detached canvas caller still pins its native pivot and exact extent");
        }
        File.WriteAllText(Path.Combine(_output, "hover645-layout-matrix.txt"),
            "4 native top/center/stretch/pivot layouts; header present/absent; detached canvas pivot/extent/pose pass\n");
    }

    private static IEnumerator FullHover645()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 10;
        Transform shared = Go("Full native owner").transform;
        Transform remoteFrame = Go("Full native observer").transform;
        remoteFrame.position = Vector3.right * 4f;
        var panel = Rect("Enhancement original viewport", shared, Vector2.zero, new Vector2(512f,1080f));
        panel.localScale = Vector3.one * .002f;
        panel.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var scroll = Rect("Native Scroll Content", panel, Vector2.zero, new Vector2(512f,1600f));
        scroll.anchorMin = scroll.anchorMax = new Vector2(.5f,1f); scroll.pivot = new Vector2(.5f,1f);
        var rows = new RectTransform[3];
        for (int i=0;i<rows.Length;i++)
        {
            rows[i]=(RectTransform)NativeRow632(scroll,i==0?"Wunde":i==1?"Verwirrung":"Entwaffnen");
            rows[i].sizeDelta = new Vector2(512f,70f);
            rows[i].anchorMin = rows[i].anchorMax = new Vector2(.5f,1f);
            rows[i].anchoredPosition3D = new Vector3(0f,-35f-70f*i,0f);
        }
        Func<Transform,bool> omit=node=>rows.Contains(node);
        TownServiceMirror.RegisterTemplate(3,1,panel,omit,"enchant.inventory|");
        for (int i=0;i<rows.Length;i++) TownServiceMirror.RegisterTemplate(3,(ushort)(2+i),rows[i],address:"enchant.row|"+i);
        TownServiceMirror.BeginSession(3,1645,shared,panel); TownServiceMirror.SetLocalTransactionActive(3,true);
        TownServiceMirror.RegisterModule(10,1,panel,omit,"enchant.inventory|");
        for (int i=0;i<rows.Length;i++) { TownServiceMirror.RegisterModule((ushort)(11+i),(ushort)(2+i),rows[i],address:"enchant.row|"+i); TownServiceMirror.SetPriority((ushort)(11+i),true); }
        TownServiceMirror.FastMotionCaptureEnabled = true; TownServiceMirror.SharedFrameForRemote=_=>remoteFrame;
        var cold=CaptureFast(); Receive(2,cold.Artwork); DeliverMotion(2,cold);
        var settle=FastSettle(remoteFrame,.15f);while(settle.MoveNext())yield return settle.Current;
        Check(Remote(2,11)!=null,"actual original enhancement rows are admitted through the production receiver");
        Check(ComposedKinds645(Remote(2, 11)!).Contains(1), "initial native row composition contains its genuine compact root header");
        // A native caption/artwork refresh advances the complete frame while
        // its stationary root still has an older independent numeric sample.
        rows[0].GetComponentsInChildren<TMPro.TMP_Text>(true).First(n=>n.name=="Name").text="Wunde — current native caption";
        float nextTime=Time.unscaledTime+.09f;
        while(Time.unscaledTime<nextTime) yield return null;
        var refreshed=CaptureFast();Receive(2,refreshed.Artwork);DeliverMotion(2,refreshed);TownServiceMirror.TickRemote(_=>remoteFrame);
        Check(refreshed.Artwork.Count!=0,"native caption refresh really advances the production complete frame");
        nextTime=Time.unscaledTime+.09f;
        while(Time.unscaledTime<nextTime) { TownServiceMirror.TickRemote(_=>remoteFrame);yield return null; }
        var hover=rows[0].GetComponentsInChildren<RectTransform>(true).First(n=>n.name=="Content");
        hover.anchoredPosition3D += Vector3.right*14f;
        var numbers=CaptureFast();Receive(2,numbers.Artwork);DeliverMotion(2,numbers);TownServiceMirror.TickRemote(_=>remoteFrame);
        List<byte> kinds = ComposedKinds645(Remote(2, 11)!);
        Check(kinds.Contains(2) && !kinds.Contains(1),
            "caption refresh really filters the stale stationary root while admitting current native child-hover numbers");
        File.WriteAllText(Path.Combine(_output, "hover645-composed-kinds.txt"), string.Join(",", kinds) + "\n");
        float stationaryWorst = 0f;
        for(int sub=0;sub<12;sub++)
        {
            TownServiceMirror.TickRemote(_=>remoteFrame);
            Vector3 nativeWorld=remoteFrame.TransformPoint(shared.InverseTransformPoint(rows[0].position));
            float rootError=Mathf.Abs(Remote(2,11)!.Root.position.y-nativeWorld.y);
            File.AppendAllText(Path.Combine(_output,"hover-root-state.txt"),"sub="+sub+";dy="+rootError+";anchor="+((RectTransform)Remote(2,11)!.Root).anchorMin+"\n");
            stationaryWorst = Mathf.Max(stationaryWorst, rootError);
            yield return null;
        }
        // Batch-mode frames can run much faster than a headset's render clock.
        // Allow the intended horizontal hover to reach its actual time endpoint
        // before comparing camera readbacks; every intermediate Y was checked.
        settle = FastSettle(remoteFrame, .15f); while (settle.MoveNext()) yield return settle.Current;
        Vector3 settledNative = remoteFrame.TransformPoint(shared.InverseTransformPoint(rows[0].position));
        stationaryWorst = Mathf.Max(stationaryWorst, Mathf.Abs(Remote(2, 11)!.Root.position.y - settledNative.y));
        Vector3 ownerCenter = rows[1].position;
        Vector3 observerCenter = remoteFrame.TransformPoint(shared.InverseTransformPoint(ownerCenter));
        Color32[] ownerPixels = RenderHover645(panel, ownerCenter, panel.rotation, 8, "hover-native-owner");
        Color32[] observerPixels = RenderHover645(Remote(2, 10)!.Root, observerCenter, panel.rotation, 9, "hover-native-observer");
        int differing = 0, ink = 0;
        for (int pixel = 0; pixel < ownerPixels.Length; pixel++)
        {
            Color32 a = ownerPixels[pixel], b = observerPixels[pixel];
            if (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) > 30) differing++;
            if (Math.Abs(a.r - 6) + Math.Abs(a.g - 8) + Math.Abs(a.b - 10) > 30) ink++;
        }
        File.WriteAllText(Path.Combine(_output, "hover645-render-metrics.txt"),
            "768x512 actual native three-row renders; differingRGB>30=" + differing + ";ownerInk=" + ink + "\n");
        Check(stationaryWorst<.00001f,"hover after an original artwork refresh cannot move a stationary native row vertically; error="+stationaryWorst);
        Check(ink > 3000, "native owner menu camera readback contains real original row content");
        Check(differing < ownerPixels.Length * .001f, "native menu owner/observer renders agree after stationary hover settles");
        RectTransform seenContent = Remote(2, 11)!.Root.GetComponentsInChildren<RectTransform>(true).First(n => n.name == "Content");
        Check(Vector3.Distance(seenContent.anchoredPosition3D, hover.anchoredPosition3D) < .001f,
            "original 14px horizontal hover is retained exactly after its normal interpolation endpoint");
        float worst=0f;
        for (int step=0;step<36;step++)
        {
            // Actual native scroll and14pxContent hover run concurrently; a
            // separate numeric/header event may arrive between artwork frames.
            scroll.anchoredPosition3D = new Vector3(0f,step<12?step*11f:132f,0f);
            panel.sizeDelta = new Vector2(step%2==0?512f:530f,1080f);
            for(int i=0;i<rows.Length;i++)
            {
                var content=rows[i].GetComponentsInChildren<RectTransform>(true).First(n=>n.name=="Content");
                content.anchoredPosition3D = new Vector3(-70f+(step%3==i?14f:0f),0f,0f);
                rows[i].GetComponentsInChildren<Graphic>(true).First(n=>n.name=="Name").color=step%3==i?Color.yellow:Color.white;
            }
            float until=Time.unscaledTime+.085f;
            while(Time.unscaledTime<until)
            {
                var next=CaptureFast();Receive(2,next.Artwork);DeliverMotion(2,next);TownServiceMirror.TickRemote(_=>remoteFrame);
                for(int i=0;i<rows.Length;i++)
                {
                    TownServiceBinding actual=Remote(2,(ushort)(11+i))!;
                    if(step>15)
                    {
                        Vector3 nativeWorld=remoteFrame.TransformPoint(shared.InverseTransformPoint(rows[i].position));
                        float rootError=Mathf.Abs(actual.Root.position.y-nativeWorld.y);
                        Check(rootError<.005f,"hover after settled native scroll cannot independently move an original row through its neighbours; step="+step+"; row="+i+"; error="+rootError);
                    }
                    using var expected=new TownServiceBinding(rows[i]);
                    // Parent motion can be delayed by one native sample, but
                    // never independently move a row through another row.
                    for(int j=1;j<actual.Nodes.Length;j++)
                    {
                        if(actual.Nodes[j] is not RectTransform rect || expected.Nodes[j] is not RectTransform native) continue;
                        float error=Mathf.Abs(rect.anchoredPosition3D.y-native.anchoredPosition3D.y);
                        worst=Mathf.Max(worst,error);
                        Check(error<.002f,"full native hover/scroll cannot displace original row glyphs vertically; step="+step+"; node="+rect.name+"; error="+error);
                    }
                }
                yield return null;
            }
        }
        settle=FastSettle(remoteFrame,.15f);while(settle.MoveNext())yield return settle.Current;
        File.WriteAllText(Path.Combine(_output,"hover645-full-metrics.txt"),"36 native scroll/hover/fit samples, actual capture/codec/motion/binding receiver; maxY="+worst+"px\n");
    }

    private static IEnumerator Hover645()
    {
        var live = FullHover645(); while (live.MoveNext()) yield return live.Current;
        TownServiceMirror.Shutdown();
        LayoutMatrix645();
        var owner = Rect("Native hover owner", null!, Vector2.zero, new Vector2(1000f, 1000f));
        owner.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)NativeRow632(owner, "Wunde");
        root.anchorMin = root.anchorMax = root.pivot = Vector2.one * .5f;
        root.sizeDelta = new Vector2(640f, 100f);
        root.anchoredPosition3D = Vector3.zero;
        RectTransform content = root.GetComponentsInChildren<RectTransform>(true).First(node => node.name == "Content");
        Vector3 nativeRest = content.anchoredPosition3D;
        var host = Rect("Native hover observer host", null!, Vector2.zero, owner.sizeDelta);
        host.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var clone = Object.Instantiate(root.gameObject, host, false);
        clone.SetActive(false); TownServiceNeutralize.Apply(clone); clone.SetActive(true);
        using var source = new TownServiceBinding(root);
        using var observer = new TownServiceBinding(clone.transform);
        var motion = new TownServiceMotion(host, observer.Nodes, "enchant.row|");
        Check(observer.Nodes.Length == 26, "the actual native enhancement prefab retains all26 original nodes");
        Check(!clone.GetComponentsInChildren<Behaviour>(true).Any(component => component is LayoutGroup || component is Animator),
            "observer has no autonomous native layout or animator writes");
        TownServiceFrame initial = HoverFrame645(source, 1, 0f);
        motion.BeforeApply(0f); observer.Apply(initial, TownServiceMirror.Assets); motion.AfterApply(0f, .1f, sourceSampleTime: 0f);
        Canvas.ForceUpdateCanvases();
        float worst = 0f;
        // The original ExtendedButton targets Content, moves it14px only in X,
        // and retains highlightScaleFactor1. These native serialized values were
        // read from sharedassets4/assets226/controller1271, not invented UI.
        for (int cycle = 0; cycle < 12; cycle++)
        {
            float begin = cycle * .3f + .1f;
            root.sizeDelta = new Vector2(640f + cycle % 3 * 90f, cycle % 2 == 0 ? 160f : 75f);
            content.anchoredPosition3D = nativeRest + (cycle % 2 == 0 ? Vector3.right * 14f : Vector3.zero);
            TownServiceFrame changed = HoverFrame645(source, (ulong)(2 + cycle * 2), begin);
            motion.BeforeApply(begin); observer.Apply(changed, TownServiceMirror.Assets);
            motion.AfterApply(begin, .1f, sourceSampleTime: begin);
            // A separate hover tint arrives while the parent's native rect is
            // still moving. Original child transforms are unchanged and Binding
            // must therefore legitimately skip those values on this pass.
            float middle = begin + .037f;
            motion.Tick(middle);
            root.GetComponentsInChildren<Graphic>(true).First(graphic => graphic.name == "Name").color
                = cycle % 2 == 0 ? Color.yellow : Color.white;
            TownServiceFrame tint = HoverFrame645(source, (ulong)(3 + cycle * 2), middle);
            motion.BeforeApply(middle); observer.Apply(tint, TownServiceMirror.Assets);
            motion.AfterApply(middle, .037f, sourceSampleTime: middle);
            for (int sub = 0; sub <= 20; sub++)
            {
                motion.Tick(middle + .007f * sub); Canvas.ForceUpdateCanvases();
                for (int nodeIndex = 0; nodeIndex < observer.Nodes.Length; nodeIndex++)
                {
                    Transform node = observer.Nodes[nodeIndex];
                    if (node == observer.Root || node is not RectTransform actual) continue;
                    string path = Relative645(observer.Root, node);
                    var expected = (RectTransform)source.Nodes[nodeIndex];
                    // Native anchored intent must never acquire a Y/Z offset
                    // from the independently interpolated ancestor rect.
                    float error = Mathf.Abs(actual.anchoredPosition3D.y - expected.anchoredPosition3D.y)
                        + Mathf.Abs(actual.anchoredPosition3D.z - expected.anchoredPosition3D.z);
                    worst = Mathf.Max(worst, error);
                    Check(error < .001f,
                        "native hover cannot displace row glyphs vertically when another native rect/color sample arrives; "
                        + "node=" + path + "; cycle=" + cycle + "; sub=" + sub + "; error=" + error.ToString("F5"));
                }
            }
            for (int nodeIndex = 0; nodeIndex < observer.Nodes.Length; nodeIndex++)
            {
                Transform node = observer.Nodes[nodeIndex];
                if (node is not RectTransform actual) continue;
                var expected = (RectTransform)source.Nodes[nodeIndex];
                var a = new Vector3[4]; var b = new Vector3[4]; actual.GetWorldCorners(a); expected.GetWorldCorners(b);
                for (int corner = 0; corner < 4; corner++) Check(Vector3.Distance(a[corner], b[corner]) < .005f,
                    "every original native row corner converges to the owner after concurrent hover/layout");
            }
        }
        File.WriteAllText(Path.Combine(_output, "hover645-metrics.txt"),
            "Native26-node row;12 alternating parentfit/hover/tint cycles;252 intermediate samples; maximum unwantedYZ=" + worst + "px\n");
        yield return null;
    }
}
