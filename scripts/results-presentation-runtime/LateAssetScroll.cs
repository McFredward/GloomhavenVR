using System;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class ProgressionTests
{
    // Source permission/input delivery is covered by shared-window-scroll-runtime.
    // Here a delivered host/guest scroll executes the original ScrollRect callback,
    // then the complete source -> delta/codec -> original material binding -> inert
    // picture path. The observer survives both updates: a cold rebuild hid this bug.
    static void LateFlowmapScroll(Camera camera)
    {
        footerProof = true;
        float priorView = camera.orthographicSize;
        camera.orthographicSize = 1.2f;
        FFSNetwork.IsClient = false;
        var events = new GameObject("LateFlowmapEventSystem", typeof(EventSystem));
        var author = Make(camera, "LateFlowmapAuthor");
        WireScroll(author, 16);
        NativeOptions(author, false, () => { }, () => { });
        Select(author);
        author.Manager.BeginNativeReady(() => { });
        author.Ready.SetInteractable(true);
        author.Host.sizeDelta = new Vector2(1458, 878);
        author.Root.anchoredPosition = new Vector2(19, -73);
        ScenarioResultsReadyPark.Tick();
        var scroll = author.Root.GetComponentInChildren<ScrollRect>(true);
        scroll.scrollSensitivity = 340;
        scroll.verticalNormalizedPosition = 1;
        Canvas.ForceUpdateCanvases();
        using var authorBar = new GrabbableModal(author.Panel, new Rect(-620, -450, 1000, 800), -500);
        authorBar.Apply();
        var source = new ScenarioResultsPresentation();
        ResultsClock.Now += .3f; ResultsClock.Frame++;
        var basis = State(221, 1, ResultsClock.Now);
        source.Capture(basis);
        const string dependency = "texture|T_flowmap_outwards_blur|256|256|12|9";
        Check(basis.Parts.SelectMany(p => p.Nodes).SelectMany(n => n.Values.Values)
            .Any(v => v.Text.Contains(dependency)),
            "the complete original Results material really requires the audited native flowmap");
        var firstPixels = Image(camera, "late-flowmap-initial-author.png");
        authorBar.SetVisible(false);
        Check(!firstPixels.SequenceEqual(Image(camera, "late-flowmap-initial-author-without-bar.png")),
            "the complete final camera image really contains the drawn Results bar");
        authorBar.SetVisible(true);
        author.Canvas.gameObject.SetActive(false);
        var observer = Make(camera, "LateFlowmapObserver");
        WireScroll(observer, 16);
        observer.Root.GetComponentInChildren<ScrollRect>(true).content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 360);
        NativeOptions(observer, false, () => { }, () => { });
        observer.Host.sizeDelta = new Vector2(800, 500);
        observer.Root.anchoredPosition = new Vector2(83, -29);
        observer.Canvas.localScale = Vector3.one * .002f;
        observer.Host.localScale = Vector3.one * .5f;
        observer.Ready.gameObject.SetActive(false);
        observer.Ready.Window.IsOpen = false;
        var receiver = new ScenarioResultsPresentation();
        Select(observer); FFSNetwork.IsClient = true;
        var initialPacket = ScenarioResultsCodec.Write(basis);
        Check(ScenarioResultsCodec.TryRead(initialPacket, initialPacket.Length, out var received) && received != null,
            "initial common Results reaches the original presentation codec");
        receiver.Apply(received!); ResultsClock.Frame++; receiver.Tick();
        Check(observer.Panel.ExtraRenderRoots.Count == 1, "initial late-flowmap picture is accepted: "
            + typeof(ScenarioResultsPresentation).GetField("_lastFailure", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(receiver));
        var picture = observer.Panel.ExtraRenderRoots.Single();
        using var observerBar = new GrabbableModal(observer.Panel, new Rect(-180, -900, 500, 1800), -1100);
        observerBar.Apply();
        CheckBarPair(authorBar, observerBar);
        CheckPixels(firstPixels, Image(camera, "late-flowmap-initial-observer.png"),
            "initial complete common Results pixels agree before a second wrapper exists");

        var assets = (TownServiceAssets)typeof(ScenarioResultsPresentation)
            .GetField("_assets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(receiver)!;
        var original = assets.Resolve<Texture2D>(dependency);
        var duplicate = NativeMaterials.DuplicateFlowmap();
        Check(original == NativeMaterials.Flowmap && duplicate != original
            && duplicate.GetRawTextureData().SequenceEqual(original!.GetRawTextureData()),
            "the second late wrapper retains the exact audited original native mip stream");
        // Match the real best-effort Resources census: it catches Key's exception
        // but the old registry permanently poisoned Resolve for every later frame.
        typeof(TownServiceAssets).GetField("_nextScan", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(assets, 0f);
        assets.Scan();
        var previousPixels = firstPixels;
        for (int step = 0; step < 2; step++)
        {
            observer.Canvas.gameObject.SetActive(false);
            author.Canvas.gameObject.SetActive(true);
            Select(author); FFSNetwork.IsClient = false;
            var pointer = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -1) };
            float before = scroll.content.anchoredPosition.y;
            ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.scrollHandler);
            Canvas.ForceUpdateCanvases(); ScenarioResultsReadyPark.Tick(); authorBar.Apply();
            Check(scroll.content.anchoredPosition.y > before + 100,
                step == 0 ? "host scroll changes the actual original content" : "delivered guest scroll changes the actual original content");
            ResultsClock.Now += .3f; ResultsClock.Frame++;
            var state = State(221, (ulong)(step + 2), ResultsClock.Now);
            source.Capture(state);
            var delta = ScenarioResultsDelta.Create(basis, state);
            Check(delta != null && delta.BaseRevision == basis.Revision,
                "each scroll is a sparse native update against the original full baseline");
            var packet = ScenarioResultsCodec.Write(delta!);
            File.WriteAllBytes(Path.Combine(evidence, "late-flowmap-scroll-" + step + ".bin"), packet);
            Check(ScenarioResultsCodec.TryRead(packet, packet.Length, out var decoded) && decoded != null,
                "each scroll delta crosses the unchanged original codec");
            var expanded = ScenarioResultsDelta.Expand(received, decoded!);
            Check(expanded != null && ScenarioResultsCodec.Write(expanded).SequenceEqual(ScenarioResultsCodec.Write(state)),
                "each received scroll expands to every exact source property");
            var expected = Image(camera, "late-flowmap-scroll-" + step + "-author.png");
            Check(!expected.SequenceEqual(previousPixels), "each scroll changes the final native camera pixels");
            var expectedGeometry = ScrollGeometry(author.Host, author.Root, (RectTransform)author.Ready.transform);
            author.Canvas.gameObject.SetActive(false);
            observer.Canvas.gameObject.SetActive(true);
            Select(observer); FFSNetwork.IsClient = true;
            receiver.Apply(expanded!); ResultsClock.Frame++; receiver.Tick();
            ResultsClock.Now += .3f; ResultsClock.Frame++; receiver.Tick();
            var displayed = (ScenarioResultsState?)typeof(ScenarioResultsPresentation)
                .GetField("_displayed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(receiver);
            Check(displayed?.Revision == state.Revision,
                step == 0 ? "late native flowmap duplicate must not freeze host-authored scroll on observer"
                    : "late native flowmap duplicate must not freeze guest-authored scroll on observer");
            Check(ReferenceEquals(observer.Panel.ExtraRenderRoots.Single(), picture),
                "both scrolls update the existing inert picture without a structural replacement");
            observerBar.Apply(); CheckBarPair(authorBar, observerBar);
            var actual = Image(camera, "late-flowmap-scroll-" + step + "-observer.png");
            CheckPixels(expected, actual, "host and guest scroll updates reach matching final observer pixels after late native discovery");
            Check(!actual.SequenceEqual(previousPixels), "observer pixels visibly advance after each host and guest scroll");
            var root = (RectTransform)picture.GetChild(0);
            var footer = picture.GetComponentsInChildren<ScenarioResultsContinuationProxy>(true)
                .Single(x => x.Operation == ScenarioResultsNative.ContinueReady);
            var actualGeometry = ScrollGeometry(observer.Host, root, (RectTransform)footer.transform);
            for (int i = 0; i < actualGeometry.Length; i++)
                Check(Vector2.Distance(actualGeometry[i].center, expectedGeometry[i].center) < .0001f
                    && Vector2.Distance(actualGeometry[i].size, expectedGeometry[i].size) < .0001f,
                    "persistent common frame retains normalized header viewport content scrollbar and footer geometry after scroll " + i);
            Check(observer.Host.rect.size == state.FrameSize,
                "both accepted updates retain the author's complete shared frame size");
            Check(assets.Resolve<Texture2D>(dependency) == original,
                "late discovery still resolves the same audited original native material binding");
            previousPixels = expected;
        }
        // Shared pose/resize adoption is an explicit port here; both final host
        // transforms and the unchanged late bar writer execute on the common pose.
        author.Host.position += new Vector3(.08f, .04f, 0);
        author.Host.rotation = Quaternion.Euler(0, 0, 4);
        author.Host.localScale *= 1.25f;
        observer.Host.SetPositionAndRotation(author.Host.position, author.Host.rotation);
        observer.Host.localScale *= 1.25f;
        authorBar.Apply(1.25f); observerBar.Apply(1.25f); CheckBarPair(authorBar, observerBar);
        observer.Canvas.gameObject.SetActive(false); author.Canvas.gameObject.SetActive(true);
        Select(author); FFSNetwork.IsClient = false; Canvas.ForceUpdateCanvases();
        ResultsClock.Now += .3f; ResultsClock.Frame++;
        var resizedState = State(221, 4, ResultsClock.Now); source.Capture(resizedState);
        var resizedPacket = ScenarioResultsCodec.Write(resizedState);
        Check(ScenarioResultsCodec.TryRead(resizedPacket, resizedPacket.Length, out var resizedRemote) && resizedRemote != null,
            "native geometry after manual resize still crosses the original full presentation codec");
        var resized = Image(camera, "late-flowmap-manual-resize-author.png");
        author.Canvas.gameObject.SetActive(false); observer.Canvas.gameObject.SetActive(true);
        Select(observer); FFSNetwork.IsClient = true; receiver.Apply(resizedRemote!);
        ResultsClock.Frame++; receiver.Tick(); ResultsClock.Now += .3f; ResultsClock.Frame++; receiver.Tick();
        observerBar.Apply(1.25f); CheckBarPair(authorBar, observerBar);
        CheckPixels(resized, Image(camera, "late-flowmap-manual-resize-observer.png"),
            "manual common movement and resize preserve complete content and drawn bar pixels at independent canvas scales");
        authorBar.SetVisible(false); observerBar.SetVisible(false);
        using (var legacy = new GrabbableModal(observer.Panel, new Rect(-180, -900, 500, 1800), -1100,
            kind: SharedWindowKind.ScenarioStory))
        {
            legacy.Apply();
            Check(Mathf.Abs(legacy.TargetPosition.x - .0875f) < .0001f && legacy.TargetPosition.y < -1.3f,
                "non-Results shared windows retain the original private ink span centre and lower clearance");
        }
        using (var privateResults = new GrabbableModal(observer.Panel, new Rect(-180, -900, 500, 1800), -1100,
            shared: false))
        {
            privateResults.Apply();
            Check(Mathf.Abs(privateResults.TargetPosition.x - .0875f) < .0001f && privateResults.TargetPosition.y < -1.3f,
                "unshared Results retain the original private ink rule");
        }
        receiver.Reset(); source.Reset();
        author.Manager.myWindow.Hide();
        Select(author); ScenarioResultsReadyPark.Tick();
        Object.DestroyImmediate(author.Canvas.gameObject); Object.DestroyImmediate(observer.Canvas.gameObject);
        Object.DestroyImmediate(duplicate); Object.DestroyImmediate(events);
        FlowmapRefusals(); footerProof = false; camera.orthographicSize = priorView;
    }

    static void CheckBarPair(GrabbableModal author, GrabbableModal observer)
    {
        Check(Vector3.Distance(author.DrawnBounds.center, observer.DrawnBounds.center) < .0001f
            && Vector3.Distance(author.DrawnBounds.size, observer.DrawnBounds.size) < .0001f
            && Vector3.Distance(author.ZoneCentre, observer.ZoneCentre) < .0001f,
            "Results author and observer bars ignore divergent private ink width centre and lower extent");
        Check(Mathf.Abs(author.TargetPosition.x) < .0001f && Mathf.Abs(observer.TargetPosition.x) < .0001f,
            "both Results bars remain centred under the common frame rather than either local content union");
    }

    static Rect[] ScrollGeometry(RectTransform host, RectTransform root, RectTransform footer)
    {
        // Neutralized copies retain their authored component fields but cannot run
        // the native scroll controller. Resolve the same original hierarchy paths.
        var originalScroll = Find(root, NativePaths.statsPanel).Find("Scroll View");
        var viewport = (RectTransform)originalScroll.Find("Viewport");
        var content = (RectTransform)viewport.Find("Content");
        var bar = (RectTransform)originalScroll.Find("Scrollbar Vertical");
        return new[] { (RectTransform)Find(root, NativePaths.header), viewport, content, bar, footer }
            .Select(rect => { var b = Bounds(host, rect); return new Rect(b.x / host.rect.width,
                b.y / host.rect.height, b.width / host.rect.width, b.height / host.rect.height); }).ToArray();
    }

    static void CheckPixels(byte[] expected, byte[] actual, string message)
    {
        int differences = 0;
        for (int i = 0; i < actual.Length; i++) if (Math.Abs(actual[i] - expected[i]) > 4) differences++;
        Check(differences < expected.Length * .003, message);
    }

    static void FlowmapRefusals()
    {
        foreach (var d in new[] { new[] { 128, 256, (int)TextureFormat.DXT5, 1 },
            new[] { 256, 256, (int)TextureFormat.RGBA32, 1 }, new[] { 256, 256, (int)TextureFormat.DXT5, 0 } })
        {
            var original = new Texture2D(d[0], d[1], (TextureFormat)d[2], d[3] != 0) { name = "T_flowmap_outwards_blur" };
            var duplicate = Object.Instantiate(original); duplicate.name = original.name;
            var registry = new TownServiceAssets(); registry.Key(original);
            bool refused = false;
            try { registry.Key(duplicate); } catch (InvalidDataException) { refused = true; }
            Check(refused, "same-name flowmap with unaudited dimensions format or mip count remains refused");
            Object.DestroyImmediate(original); Object.DestroyImmediate(duplicate);
        }
        var unknown = new Texture2D(256, 256, TextureFormat.DXT5, true) { name = "UnknownFlowmap" };
        var other = Object.Instantiate(unknown); other.name = unknown.name;
        var assets = new TownServiceAssets(); string key = assets.Key(unknown); bool blocked = false;
        try { assets.Key(other); } catch (InvalidDataException) { blocked = true; }
        Check(blocked, "unknown same-descriptor flowmap duplicates still poison required resolution");
        blocked = false;
        try { assets.Resolve<Texture2D>(key); } catch (InvalidDataException) { blocked = true; }
        Check(blocked, "unknown ambiguous originals cannot silently resolve an arbitrary native texture");
        Object.DestroyImmediate(unknown); Object.DestroyImmediate(other);
    }
}
