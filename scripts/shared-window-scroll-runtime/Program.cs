using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class InnerScrollButton : Button
{
    public int DownCalls;
    public override void OnPointerDown(PointerEventData data) { DownCalls++; base.OnPointerDown(data); }
}

public static class SharedScroll762Program
{
    public static IEnumerator Run(Action<int> report) => Program.Run(report);
}

internal static class Program
{
    internal static readonly Dictionary<int, Peer> Peers = new();
    internal static readonly HashSet<int> Unmodded = new();
    private static int _checks;
    private static float _clock;
    private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }
    private static bool Near(float a, float b) => Mathf.Abs(a - b) < .002f;
    private static RectTransform Rect(string name, Transform? parent, Vector2 size)
    { var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform; r.SetParent(parent, false); r.sizeDelta = size; return r; }
    internal sealed class Transport : INetTransport
    { internal int Id; public int LocalPlayerId => Id; public bool IsOnline => FFSNetwork.IsOnline; }
    internal sealed class Peer
    {
        internal readonly int Id;
        internal bool Map = true;
        internal readonly SharedWindowScrollCoordinator Core = new();
        internal readonly Transport Transport;
        internal readonly UIWindow?[] Windows = new UIWindow?[8];
        internal readonly List<(byte[] Data, bool HostOnly)> Sent = new();
        internal readonly Camera Camera;
        internal readonly Canvas Canvas;
        internal readonly GraphicRaycaster Raycaster;
        internal readonly RectTransform Root;
        internal readonly RectTransform Results;
        internal readonly RectTransform Header;
        internal readonly RectTransform[] Repeated;
        internal ScrollRect Scroll;
        internal readonly Scrollbar Bar;
        internal UICharacterStoryBox? Story;
        internal bool FailSend;
        internal Peer(int id, byte kind = 7, float height = 1200)
        {
            Id = id; Transport = new Transport { Id = id };
            Root = Rect("Original window", null, new Vector2(600, 400)); Root.position = new Vector3(id * 1000, 0, 0);
            Windows[kind] = Root.gameObject.AddComponent<UIWindow>();
            Camera = new GameObject("Original camera " + id, typeof(Camera)).GetComponent<Camera>();
            Camera.enabled = false; Camera.orthographic = true; Camera.orthographicSize = 250;
            Camera.transform.position = new Vector3(id * 1000, 0, -1000); Camera.farClipPlane = 2000;
            Camera.targetTexture = new RenderTexture(600, 500, 16);
            Canvas = Root.gameObject.AddComponent<Canvas>(); Canvas.renderMode = RenderMode.WorldSpace; Canvas.worldCamera = Camera;
            Raycaster = Root.gameObject.AddComponent<GraphicRaycaster>();
            UguiPokeSurfaces.Register(Canvas);
            Header = Rect("Header Highlight", Root, new Vector2(400, 40));
            Header.anchoredPosition = new Vector2(0, 160); Header.gameObject.AddComponent<Image>();
            Header.gameObject.AddComponent<RectMask2D>();
            Rect("Original oversized header glow", Header, new Vector2(450, 40)).gameObject.AddComponent<Image>();
            Repeated = new[] { Rect("Native row(Clone)", Root, new Vector2(20, 20)),
                Rect("Native row(Clone)", Root, new Vector2(20, 20)) };
            Repeated[0].anchoredPosition = new Vector2(-200, -160);
            Repeated[1].anchoredPosition = new Vector2(200, -160);
            Results = Rect("Results", Root, new Vector2(600, 400));
            var holder = Rect("Public scroll", Results, new Vector2(400, 200));
            var viewport = Rect("Viewport", holder, new Vector2(400, 200)); viewport.gameObject.AddComponent<Image>();
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport, new Vector2(400, height));
            content.pivot = new Vector2(.5f, 1); content.anchorMin = content.anchorMax = new Vector2(.5f, 1);
            var ink = Rect("Visible source row", content, new Vector2(350, 80)); ink.gameObject.AddComponent<Image>();
            for (int i = 0; i < 12; i++)
            {
                var stripe = Rect("Author row " + i, content, new Vector2(350, 100));
                stripe.anchorMin = stripe.anchorMax = new Vector2(.5f, 1);
                stripe.anchoredPosition = new Vector2(0, -50 - i * 100);
                stripe.gameObject.AddComponent<Image>().color = new Color(i / 12f, 1f - i / 12f, i % 2, 1);
            }
            Scroll = holder.gameObject.AddComponent<ScrollRect>(); Scroll.content = content; Scroll.viewport = viewport;
            Scroll.horizontal = false; Scroll.vertical = true; Scroll.scrollSensitivity = 40; Scroll.inertia = false;
            Scroll.movementType = ScrollRect.MovementType.Clamped;
            var bar = Rect("Scrollbar", holder, new Vector2(25, 200)); bar.anchoredPosition = new Vector2(225, 0);
            var area = Rect("Sliding area", bar, new Vector2(25, 200));
            var handle = Rect("Handle", area, new Vector2(25, 40)); var image = handle.gameObject.AddComponent<Image>();
            Bar = bar.gameObject.AddComponent<Scrollbar>(); Bar.handleRect = handle; Bar.targetGraphic = image;
            Bar.direction = Scrollbar.Direction.BottomToTop; Scroll.verticalScrollbar = Bar;
            Scroll.verticalNormalizedPosition = 1;
            if (kind == 2) Story = holder.gameObject.AddComponent<UICharacterStoryBox>();
            Peers[id] = this; Select();
        }
        internal void Select()
        {
            SharedWindows.Native = Windows; SharedWindows.MapActive = Map;
            SharedWindows.Header = Header;
            Singleton<MapStoryController>.Instance = Story == null ? null! : new MapStoryController { dialogBox = Story, window = Windows[2] };
            Core.Bind(Transport, (bytes, length, hostOnly) => {
                if (FailSend) return false; var exact = new byte[length]; Array.Copy(bytes, exact, length); Sent.Add((exact, hostOnly)); return true; });
            Core.Tick(Time.unscaledTime);
        }
        internal void Publish() { Select(); Core.LateTick(_clock += .1f); }
        internal void Deliver(Peer target)
        {
            target.Select();
            foreach (var packet in Sent)
                if (!packet.HostOnly || target.Id == FFSNet.PlayerRegistry.HostPlayerID)
                    Check(target.Core.Receive(Id, packet.Data, packet.Data.Length), "the actual source codec routes each complete public packet");
            Sent.Clear();
        }
        internal void Paint() { Select(); Core.Tick(Time.unscaledTime); }
    }
    private static SharedWindowScrollMessage FirstState(Peer peer, byte control = 1)
    {
        foreach (var packet in peer.Sent.AsEnumerable().Reverse())
            if (SharedWindowScrollCodec.TryRead(packet.Data, packet.Data.Length, out var state)
                && state.Operation == SharedScrollOperation.State && state.Control == control) return state;
        throw new Exception("source publishes original public scroll metadata");
    }
    private static bool Aim(RayUguiDriver driver, Peer peer, Vector3 world, out RaycastResult hit)
        => driver.FixtureAim(peer.Camera.transform.position, (world - peer.Camera.transform.position).normalized, out hit);
    private static void Receive(Peer peer, int sender, SharedWindowScrollMessage message)
    { peer.Select(); byte[] bytes = SharedWindowScrollCodec.Write(message); Check(peer.Core.Receive(sender, bytes, bytes.Length), "valid control routes without native network dependency"); }

    private static void PointerRequest(Peer peer, SharedScrollInput operation, float y)
    {
        peer.Select(); var rect = (RectTransform)peer.Bar.transform;
        var data = new PointerEventData(EventSystem.current) { pointerId = -101,
            button = PointerEventData.InputButton.Left,
            pointerPressRaycast = new RaycastResult { module = peer.Raycaster, gameObject = peer.Bar.gameObject } };
        data.position = peer.Camera.WorldToScreenPoint(rect.TransformPoint(new Vector3(0,
            Mathf.LerpUnclamped(rect.rect.yMin, rect.rect.yMax, y), 0)));
        Check(SharedWindowScrollBridge.Pointer(peer.Bar, operation, data), "each participant routes its scrollbar gesture through the common original");
    }
    private static float HandleCenter(Peer host) => host.Bar.size * .5f + host.Bar.value * (1f - host.Bar.size);

    private static RectTransform PicturePart(Peer native, Transform picture, Transform part)
        => (RectTransform)SharedWindowScrollBridge.Find(picture, SharedWindowScrollBridge.Path(native.Root, part))!;
    private static float PicturePosition(Peer native, Transform picture)
    {
        RectTransform content = PicturePart(native, picture, native.Scroll.content);
        RectTransform view = PicturePart(native, picture, native.Scroll.viewport);
        var corners = new Vector3[4]; content.GetWorldCorners(corners);
        float min = corners.Min(x => view.InverseTransformPoint(x).y);
        float max = corners.Max(x => view.InverseTransformPoint(x).y);
        return (view.rect.yMin - min) / (max - min - view.rect.height);
    }
    private static Color PicturePixel(Peer peer, Transform picture)
    {
        RectTransform view = PicturePart(peer, picture, peer.Scroll.viewport);
        Vector2 point = peer.Camera.WorldToScreenPoint(view.position);
        peer.Camera.Render(); RenderTexture? previous = RenderTexture.active;
        RenderTexture.active = peer.Camera.targetTexture;
        var read = new Texture2D(1, 1, TextureFormat.RGB24, false);
        read.ReadPixels(new UnityEngine.Rect(point.x, point.y, 1, 1), 0, 0); read.Apply();
        Color color = read.GetPixel(0, 0); Object.DestroyImmediate(read); RenderTexture.active = previous;
        return color;
    }
    private static IEnumerator VisiblePictureScroll(Peer host, Peer visitor, Transform picture)
    {
        host.Select(); host.Scroll.verticalNormalizedPosition = .82f; host.Publish(); host.Deliver(visitor);
        float until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint();
        Check(Near(PicturePosition(visitor, picture), .82f),
            "accepted small author state moves visible common content without a new Results graph");
        RectTransform commonContent = PicturePart(visitor, picture, visitor.Scroll.content);
        Check(!Near(commonContent.rect.height, visitor.Scroll.content.rect.height),
            "visible projection retains author-sized content instead of private guest row bounds");
        Color before = PicturePixel(visitor, picture);
        Vector3 staleGraphPosition = commonContent.localPosition;
        var proxy = picture.GetComponentsInChildren<SharedWindowScrollProxy>(true).First(x => x.Control == 1);
        visitor.Select(); Check(SharedWindowScrollBridge.Wheel(proxy, new Vector2(0, -3)),
            "guest own visible wheel retains the common ordered request path");
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint(); visitor.Deliver(host); host.Publish();
        SharedWindowScrollMessage accepted = FirstState(host);
        float expected = host.Scroll.verticalNormalizedPosition; host.Deliver(visitor);
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint();
        Check(Near(PicturePosition(visitor, picture), expected),
            "guest sees its own accepted wheel in visible content within the small-state roundtrip");
        Color after = PicturePixel(visitor, picture);
        Check(Mathf.Abs(before.r - after.r) + Mathf.Abs(before.g - after.g) + Mathf.Abs(before.b - after.b) > .1f,
            "real rendered Results pixels change on guest own scroll before a full graph arrives");

        RectTransform handle = PicturePart(visitor, picture, visitor.Bar.handleRect);
        Check(Near(handle.anchorMin.y, host.Bar.value * (1f - host.Bar.size))
            && Near(handle.anchorMax.y - handle.anchorMin.y, host.Bar.size),
            "visible common scrollbar handle follows accepted author position and size without callbacks");
        for (int i = 0; i < 4; i++)
        {
            // A delayed whole-picture binding restores transform properties after
            // small scroll state arrived. The real post-Paint hook must repair it
            // even though normalized updates do not change proxy descriptors.
            commonContent.localPosition = staleGraphPosition;
            handle.anchorMin = Vector2.zero; handle.anchorMax = Vector2.one;
            visitor.Core.AttachResultsInputs(picture, visitor.Canvas);
            Check(Near(PicturePosition(visitor, picture), expected)
                && Near(handle.anchorMin.y, host.Bar.value * (1f - host.Bar.size)),
                "unchanged-descriptor post-Paint projection defeats a stale graph content and handle overwrite");
        }
        var stale = SharedWindowScrollCodec.Write(accepted);
        Check(SharedWindowScrollCodec.TryRead(stale, stale.Length, out var late), "accepted state can be copied for stale visible replay");
        late.Revision--; late.Y = .99f; Receive(visitor, 1, late);
        late.Revision += 1000; Receive(visitor, 3, late);
        visitor.Paint();
        Check(Near(PicturePosition(visitor, picture), expected),
            "stale or foreign small state cannot move visible common content");
        Check(picture.GetComponentsInChildren<ScrollRect>(true).Length == 0
            && picture.GetComponentsInChildren<Scrollbar>(true).Length == 0,
            "visible scroll projection installs no native controller or Selectable on the inert picture");
    }

    private static void PictureGeometry()
    {
        var source = Rect("Projection differential original", null, new Vector2(600, 400));
        var viewport = Rect("Viewport", source, new Vector2(400, 200));
        var content = Rect("Content", viewport, new Vector2(900, 1200));
        content.pivot = new Vector2(.3f, .8f);
        ScrollRect scroll = source.gameObject.AddComponent<ScrollRect>(); scroll.content = content; scroll.viewport = viewport;
        scroll.horizontal = scroll.vertical = true; scroll.inertia = false;
        var barRoot = Rect("Scrollbar", source, new Vector2(25, 200));
        var handle = Rect("Handle", barRoot, new Vector2(25, 40));
        Scrollbar bar = barRoot.gameObject.AddComponent<Scrollbar>(); bar.handleRect = handle;
        var picture = Object.Instantiate(source.gameObject).transform;
        Object.DestroyImmediate(picture.GetComponent<ScrollRect>());
        Object.DestroyImmediate(picture.GetComponentInChildren<Scrollbar>());
        RectTransform common = PicturePartFrom(source, picture, content);
        RectTransform commonHandle = PicturePartFrom(source, picture, handle);
        var projection = new SharedWindowScrollPicture();
        foreach (Vector2 size in new[] { new Vector2(900, 1200), new Vector2(200, 90) })
        {
            content.sizeDelta = common.sizeDelta = size;
            foreach (Vector2 position in new[] { Vector2.zero, new Vector2(.13f, .75f), Vector2.one })
            {
                scroll.normalizedPosition = position;
                projection.Apply(picture, source, scroll, SharedWindowScrollBridge.Path(source, viewport), position, 7, 0, 0);
                Check((common.localPosition - content.localPosition).sqrMagnitude < .001f,
                    "common picture axes and undersized pivot alignment match real Unity ScrollRect geometry");
            }
        }
        for (byte direction = 0; direction < 4; direction++)
        {
            bar.direction = (Scrollbar.Direction)direction; bar.size = .23f; bar.value = .71f;
            projection.Apply(picture, source, bar, SharedWindowScrollBridge.Path(source, barRoot), new Vector2(bar.value, 0), 1, bar.size, direction);
            Check((commonHandle.anchorMin - handle.anchorMin).sqrMagnitude < .0001f
                && (commonHandle.anchorMax - handle.anchorMax).sqrMagnitude < .0001f,
                "common handle geometry matches real Unity Scrollbar in every axis and reverse direction");
        }
        Object.DestroyImmediate(picture.gameObject); Object.DestroyImmediate(source.gameObject);
    }
    private static RectTransform PicturePartFrom(Transform original, Transform picture, Transform part)
        => (RectTransform)SharedWindowScrollBridge.Find(picture, SharedWindowScrollBridge.Path(original, part))!;

    private static IEnumerator WholeWindowInputs(Peer host, Peer visitor, Transform picture, UguiPointer pointer,
        RayUguiDriver stick, GloomhavenVR.Hands.VRHand hand)
    {
        var header = (RectTransform)SharedWindowScrollBridge.Find(picture,
            SharedWindowScrollBridge.Path(visitor.Root, visitor.Header))!;
        var wp = new ModalFallback.WindowPanel { Panel = new ModalFallback.FixturePanel
            { HostGo = visitor.Root.gameObject, HostTransform = visitor.Root, Target = null } };
        ModalFallback.FixtureConfigure(wp); GloomhavenVR.Hands.VRHands.Left = hand;
        host.Select(); host.Scroll.verticalNormalizedPosition = .5f; host.Publish(); host.Deliver(visitor);
        visitor.Paint(); visitor.Core.AttachResultsInputs(picture, visitor.Canvas);
        Canvas.ForceUpdateCanvases(); yield return null;
        Check(Aim(stick, visitor, header.position, out RaycastResult hit)
            && hit.gameObject.GetComponent<SharedResultsHeaderGraphic>() != null,
            "the real laser picker resolves the visible Results header outside the common viewport");
        visitor.Select(); int pending = visitor.Sent.Count;
        pointer.Press(visitor.Camera.WorldToScreenPoint(header.position));
        pointer.Drag(visitor.Camera.WorldToScreenPoint(header.position + new Vector3(0, -60, 0)));
        pointer.Release(visitor.Camera.WorldToScreenPoint(header.position));
        Check(visitor.Sent.Count == pending, "header hover never invents a shared viewport pointer-down or drag gesture");
        Check(!Aim(stick, visitor, header.TransformPoint(new Vector3(215, 0, 0)), out _),
            "the original header mask rejects the overflowing highlight input footprint");
        Check(!Aim(stick, visitor, header.TransformPoint(new Vector3(275, 0, 0)), out _),
            "empty common frame space outside header ink remains air");
        Check(Aim(stick, visitor, header.position, out _), "the masked header centre remains reachable after an outside laser point");
        visitor.Select(); int delivered = UiScrollFocus.Delivered; float before = host.Scroll.verticalNormalizedPosition;
        stick.FixtureTick(); ModalFallback.FixtureTick();
        float until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint(); visitor.Deliver(host);
        Check(UiScrollFocus.Delivered == delivered + 1 && host.Scroll.verticalNormalizedPosition < before,
            "Results header joystick uses the common viewport with no private native scroll target");

        visitor.Select(); wp.ResultsScroll = visitor.Scroll; visitor.Scroll.enabled = false;
        before = host.Scroll.verticalNormalizedPosition; ModalFallback.FixtureTick();
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint(); visitor.Deliver(host);
        Check(host.Scroll.verticalNormalizedPosition < before,
            "an inactive private Results list does not block common background joystick input");
        visitor.Scroll.enabled = true;
        visitor.Select(); var proxy = picture.GetComponentsInChildren<SharedWindowScrollProxy>().First(x => x.Control == 1);
        Check(Aim(stick, visitor, proxy.transform.position, out _), "common viewport hover originates in the real surface entry");
        delivered = UiScrollFocus.Delivered;
        stick.FixtureTick(); ModalFallback.FixtureTick();
        Check(UiScrollFocus.Delivered == delivered + 1,
            "viewport joystick is owned by the generic pointer without a duplicate Results fallback delivery");
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint(); visitor.Deliver(host);

        host.Select(); host.Scroll.content.sizeDelta = new Vector2(400, 200);
        Canvas.ForceUpdateCanvases(); yield return null; host.Publish(); host.Deliver(visitor);
        visitor.Paint(); visitor.Core.AttachResultsInputs(picture, visitor.Canvas);
        Check(!proxy.CanScroll, "a complete common list without overflow advertises no joystick travel");
        Check(Aim(stick, visitor, header.position, out _), "zero-travel header retains its exact visible surface");
        int hovers = UiScrollFocus.Hovers; delivered = UiScrollFocus.Delivered;
        ModalFallback.FixtureTick();
        Check(UiScrollFocus.Hovers == hovers && UiScrollFocus.Delivered == delivered,
            "non-scrollable common Results never claim locomotion focus through private overflow");
        host.Select(); host.Scroll.content.sizeDelta = new Vector2(400, 1200);
        Canvas.ForceUpdateCanvases(); yield return null; host.Publish(); host.Deliver(visitor);
        visitor.Paint(); visitor.Core.AttachResultsInputs(picture, visitor.Canvas);

        // Other/private windows and points outside this exact host retain their
        // original paths; no global modal or native input permission is added.
        Aim(stick, visitor, visitor.Root.TransformPoint(new Vector3(700, 700, 0)), out _);
        visitor.Select(); delivered = UiScrollFocus.Delivered;
        ModalFallback.FixtureTick(); Check(UiScrollFocus.Delivered == delivered,
            "hover outside the exact Results host is not redirected into the common list");
        Aim(stick, visitor, header.position, out _); wp.IsResults = false; ModalFallback.FixtureTick();
        Check(UiScrollFocus.Delivered == delivered, "private non-Results hosts retain their original fallback boundary");
        // A later native animation can hide the exact header after admission.
        // The own surface follows original visible ink, without a permanent plate.
        Graphic[] headerInk = header.GetComponentsInChildren<Graphic>().Where(x => !(x is SharedResultsHeaderGraphic)).ToArray();
        foreach (Graphic ink in headerInk) ink.color = Color.clear;
        Check(!Aim(stick, visitor, header.position, out _), "a transparent original header never leaves an invisible laser target");
        foreach (Graphic ink in headerInk) ink.color = Color.white;
        header.gameObject.SetActive(false);
        Check(!Aim(stick, visitor, header.position, out _), "an inactive original header cannot claim hover or locomotion focus");
        header.gameObject.SetActive(true);
        Check(Aim(stick, visitor, header.position, out _), "a returning original header restores the same exact hover footprint");
        var footer = Rect("Original separate footer button", visitor.Root, new Vector2(120, 40));
        footer.anchoredPosition = new Vector2(0, -160); footer.gameObject.AddComponent<Image>();
        footer.gameObject.AddComponent<Button>(); Canvas.ForceUpdateCanvases(); yield return null;
        Check(Aim(stick, visitor, footer.position, out hit) && hit.gameObject == footer.gameObject,
            "the separate original footer retains its pointer priority outside header ink");
        Object.DestroyImmediate(footer.gameObject);
        GloomhavenVR.Hands.VRHands.Left = null;
    }

    private static IEnumerator ConcurrentInputs(Peer host, Peer visitor, Peer third, Transform picture)
    {
        host.Select(); host.Scroll.verticalNormalizedPosition = .5f;
        host.Publish(); var baseline = host.Sent.ToArray(); host.Deliver(visitor);
        host.Sent.AddRange(baseline); host.Deliver(third);
        float until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint(); third.Paint();
        // Neither request bypasses the existing author, even with opposite input
        // in one visitor frame. Native ScrollRect bounds still decide the result.
        visitor.Select(); Check(SharedWindowScrollBridge.Wheel(visitor.Scroll, new Vector2(0, -2)), "first simultaneous wheel is admitted");
        third.Select(); Check(SharedWindowScrollBridge.Wheel(third.Scroll, new Vector2(0, 2)), "opposed simultaneous wheel is admitted");
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint(); visitor.Deliver(host); float first = host.Scroll.verticalNormalizedPosition;
        third.Paint(); third.Deliver(host);
        Check(first < .5f && Near(host.Scroll.verticalNormalizedPosition, .5f), "both opposed visitor wheels execute in author order on one bounded original");

        visitor.Select(); Check(SharedWindowScrollBridge.Position(visitor.Scroll, new Vector2(0, .3f)), "first simultaneous absolute position is admitted");
        third.Select(); Check(SharedWindowScrollBridge.Position(third.Scroll, new Vector2(0, .7f)), "second simultaneous absolute position is admitted");
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint(); visitor.Deliver(host); Check(Near(host.Scroll.verticalNormalizedPosition, .3f), "first absolute input acts at the common author");
        third.Paint(); third.Deliver(host); Check(Near(host.Scroll.verticalNormalizedPosition, .7f), "latest ordered absolute input determines the common position");
        host.Publish(); var common = host.Sent.ToArray(); host.Deliver(visitor);
        host.Sent.AddRange(common); host.Deliver(third);
        visitor.Select(); visitor.Core.Tick(Time.unscaledTime + .08f); third.Select(); third.Core.Tick(Time.unscaledTime + .08f);
        Check(Near(visitor.Scroll.verticalNormalizedPosition, .7f) && Near(third.Scroll.verticalNormalizedPosition, .7f),
            "simultaneous opposing requests converge on every participant");
        Check(Near(PicturePosition(visitor, picture), .7f),
            "the visible common picture converges on the same last author-ordered opposite input");

        Canvas.ForceUpdateCanvases();
        PointerRequest(visitor, SharedScrollInput.PointerDown, HandleCenter(host)); visitor.Deliver(host);
        // Both hands deliberately use the same pointer ID: only actual native
        // requester identity can distinguish the two concurrent people.
        PointerRequest(third, SharedScrollInput.PointerDown, HandleCenter(host)); third.Deliver(host);
        float owned = host.Bar.value;
        PointerRequest(visitor, SharedScrollInput.PointerDrag, .1f);
        PointerRequest(visitor, SharedScrollInput.PointerUp, .1f); visitor.Deliver(host);
        Check(Near(host.Bar.value, owned), "ended earlier player's drag cannot move a newer explicit scrollbar gesture");
        PointerRequest(third, SharedScrollInput.PointerDrag, HandleCenter(host) - .15f);
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        third.Paint(); third.Deliver(host);
        Check(host.Bar.value < owned - .02f, "stale earlier release cannot end the newer explicit scrollbar gesture");
        PointerRequest(third, SharedScrollInput.PointerUp, HandleCenter(host)); third.Deliver(host);
        float released = host.Bar.value;
        PointerRequest(visitor, SharedScrollInput.PointerDown, HandleCenter(host)); visitor.Deliver(host);
        PointerRequest(visitor, SharedScrollInput.PointerDrag, HandleCenter(host) + .1f);
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint(); visitor.Deliver(host);
        Check(host.Bar.value > released + .02f, "an earlier visitor can immediately begin a fresh explicit scrollbar gesture");
        PointerRequest(visitor, SharedScrollInput.PointerUp, HandleCenter(host)); visitor.Deliver(host);
    }

    internal static IEnumerator Run(Action<int> report)
    {
        _checks = 0; _clock = 0; Peers.Clear(); Unmodded.Clear(); FFSNetwork.IsOnline = true;
        PictureGeometry();
        SharedWindowSpawnAuthority.AuthorId = 1;
        SharedWindowScrollSync.MapParticipant = SharedWindows.MapActiveFor;
        new GameObject("Native event system", typeof(EventSystem));
        var host = new Peer(1); var visitor = new Peer(2, height: 2600); var third = new Peer(3, height: 1800);
        Canvas.ForceUpdateCanvases(); yield return null;
        foreach (Peer p in Peers.Values) { p.Scroll.verticalNormalizedPosition = 1; p.Camera.Render(); }
        visitor.Select(); float nativeBefore = visitor.Scroll.verticalNormalizedPosition;
        Check(SharedWindowScrollBridge.Wheel(visitor.Scroll, new Vector2(0, -2)), "first public wheel is consumed before descriptor without mutating private native content");
        Check(Near(nativeBefore, visitor.Scroll.verticalNormalizedPosition), "no early client mutation precedes the common original");
        host.Publish(); SharedWindowScrollMessage opening = FirstState(host); var bootstrap = host.Sent.ToArray();
        host.Deliver(visitor);
        host.Sent.AddRange(bootstrap); host.Deliver(third);
        visitor.Paint(); third.Paint();
        visitor.Select(); var pointer = new UguiPointer(); pointer.SetFixtureHover(visitor.Scroll.viewport.gameObject);
        pointer.Scroll(new Vector2(0, -3));
        Check(Near(visitor.Scroll.verticalNormalizedPosition, 1), "client wheel is intercepted before its native ScrollRect mutates");
        Check(visitor.Sent.Count > 0 && visitor.Sent[0].HostOnly, "requests for the native host use its existing reliable host-only route");
        byte[] wheel = visitor.Sent[0].Data;
        visitor.Deliver(host); float after = host.Scroll.verticalNormalizedPosition;
        Check(after < .99f, "remote wheel executes the original native ScrollRect.OnScroll at the common author");
        host.Select(); Check(host.Core.Receive(2, wheel, wheel.Length), "duplicate reliable request parses");
        Check(Near(after, host.Scroll.verticalNormalizedPosition), "duplicate nonce cannot execute the native wheel twice");
        host.Publish(); var changed = host.Sent.ToArray(); host.Deliver(visitor);
        host.Sent.AddRange(changed); host.Deliver(third);
        visitor.Select(); float receivedAt = Time.unscaledTime;
        visitor.Core.Tick(receivedAt); float beginning = visitor.Scroll.verticalNormalizedPosition;
        visitor.Core.Tick(receivedAt + .033f); float middle = visitor.Scroll.verticalNormalizedPosition;
        visitor.Camera.Render();
        Check(middle < beginning && middle > after, "observer native viewport has an intermediate smooth frame rather than15Hz snapping");
        visitor.Core.Tick(receivedAt + .08f); third.Select(); third.Core.Tick(receivedAt + .08f);
        float until;
        Check(Near(visitor.Scroll.verticalNormalizedPosition, after) && Near(third.Scroll.verticalNormalizedPosition, after),
            "three original native lists converge to the same normalized public position despite different private row heights");

        byte[] encoded = SharedWindowScrollCodec.Write(opening);
        Check(SharedWindowScrollCodec.TryRead(encoded, encoded.Length, out var forged), "source metadata is decoded before the authority counterfactual");
        forged.Revision += 1000; forged.Y = .33f;
        Receive(visitor, 3, forged); visitor.Core.Tick(Time.unscaledTime + .08f);
        Check(Near(visitor.Scroll.verticalNormalizedPosition, after), "foreign peer cannot impersonate the common author's canonical viewport state");

        // Build a complete inert common picture from the SOURCE prototype. The
        // target peer's native list intentionally has a different height/count.
        var quarantine = new GameObject("Inactive original template quarantine"); quarantine.SetActive(false);
        var sourceGroup = host.Root.gameObject.AddComponent<CanvasGroup>();
        sourceGroup.blocksRaycasts = sourceGroup.interactable = true;
        using var builder = new FixtureResultsBuilder();
        GameObject picture = builder.Construct(host.Root, new Transform[] { host.Results, host.Header, host.Repeated[0], host.Repeated[1] }, new ConvertedPanel
            { HostRect = visitor.Root, HostCanvas = visitor.Canvas, Target = visitor.Root }).gameObject;
        var mount = builder.Mount;
        // The actual Results writer attaches metadata input before its inactive
        // candidate is revealed. The same descriptor is reused after activation.
        mount.SetActive(false);
        ((RectTransform)picture.transform).anchoredPosition3D = Vector3.zero;
        picture.GetComponent<Canvas>().worldCamera = visitor.Camera;
        visitor.Select(); visitor.Core.AttachResultsInputs(picture.transform, visitor.Canvas);
        Check(picture.GetComponentsInChildren<SharedWindowScrollProxy>(true).Length == 2,
            "modular original Results paths resolve the exact viewport and scrollbar before reveal");
        for (int i = 0; i < host.Repeated.Length; i++)
        {
            Transform? repeated = SharedWindowScrollBridge.Find(picture.transform, SharedWindowScrollBridge.Path(host.Root, host.Repeated[i]));
            Check(repeated != null && repeated.name == host.Repeated[i].name
                && Near(((RectTransform)repeated).anchoredPosition.x, host.Repeated[i].anchoredPosition.x),
                "actual modular Build preserves legitimate Clone suffixes and repeated original sibling identities");
        }
        SharedWindowScrollProxy proxy = picture.GetComponentsInChildren<SharedWindowScrollProxy>(true).First(x => x.Control == 1);
        Check(picture.GetComponentsInChildren<ScrollRect>(true).Length == 0, "common picture never reinstates cloned native scroll controllers");
        Check(UguiPokeSurfaces.NestedOf(visitor.Canvas)?.Contains(picture.GetComponent<Canvas>()) == true,
            "only the exact common picture input canvas joins the existing nested-pointer census");
        mount.SetActive(true);
        foreach (Graphic native in visitor.Root.GetComponentsInChildren<Graphic>(true))
            if (!native.transform.IsChildOf(mount.transform)) native.canvasRenderer.SetAlpha(0f);
        visitor.Core.AttachResultsInputs(picture.transform, visitor.Canvas);
        Check(UguiPokeSurfaces.NestedOf(visitor.Canvas)?.Contains(picture.GetComponent<Canvas>()) == true,
            "revealing the unchanged picture retains its pre-activation nested input admission");
        Canvas.ForceUpdateCanvases(); yield return null; visitor.Camera.Render();
        var hand = new GloomhavenVR.Hands.VRHand { Thumbstick = Vector2.down };
        var stick = new RayUguiDriver(pointer, hand);
        Check(Aim(stick, visitor, proxy.transform.position, out RaycastResult viewportHit)
            && viewportHit.gameObject == proxy.gameObject,
            "real original GraphicRaycaster reaches the common viewport through inert observer groups");
        IEnumerator visibleScroll = VisiblePictureScroll(host, visitor, picture.transform);
        while (visibleScroll.MoveNext()) yield return visibleScroll.Current;
        // Restore the original baseline for the existing pointer/joystick checks.
        host.Select(); host.Scroll.verticalNormalizedPosition = after; host.Publish(); host.Deliver(visitor);
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint();
        pointer.Scroll(new Vector2(0, -2));
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Select(); visitor.Core.Tick(Time.unscaledTime); Check(visitor.Sent.Count > 0, "visible common viewport wheel creates its exact original-address request");
        visitor.Deliver(host); Check(host.Scroll.verticalNormalizedPosition < after, "common source-sized viewport forwards native wheel despite private client row differences");
        visitor.Select(); float stickBefore = host.Scroll.verticalNormalizedPosition;
        int delivered = UiScrollFocus.Delivered; stick.FixtureTick();
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint(); visitor.Deliver(host);
        Check(UiScrollFocus.Delivered > delivered && host.Scroll.verticalNormalizedPosition < stickBefore,
            "the actual pointing-hand joystick scrolls the visible common proxy through the original native author");
        host.Publish(); host.Deliver(visitor);
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint();
        Check(Near(PicturePosition(visitor, picture.transform), host.Scroll.verticalNormalizedPosition),
            "guest joystick acceptance reaches its own visible picture through the small author state");

        host.Publish(); host.Deliver(visitor); until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint(); visitor.Core.AttachResultsInputs(picture.transform, visitor.Canvas);
        SharedWindowScrollProxy barProxy = picture.GetComponentsInChildren<SharedWindowScrollProxy>(true).First(x => x.Control == 2);
        // Paired hardware has an original scrollbar 133 px outside the fitted
        // host. The original/copied canvas remains unchanged and wider.
        visitor.Root.sizeDelta = new Vector2(184, 400);
        Canvas.ForceUpdateCanvases(); yield return null;
        Vector3 barWorld = barProxy.CoordinateRoot!.TransformPoint(new Vector3(0, 50, 0));
        float outside = visitor.Root.InverseTransformPoint(barWorld).x - visitor.Root.rect.xMax;
        // The actual guest log's committed hit rect includes the outboard bar;
        // port that conversion result, then execute original surface admission.
        CanvasConversion.HitRects[visitor.Canvas] = UnityEngine.Rect.MinMaxRect(-92, -200, 250, 200);
        Check(Near(outside, 133f), "the observer scrollbar is exercised133pixels outside the fitted host frame");
        Check(Aim(stick, visitor, barWorld, out RaycastResult barHit)
            && barHit.gameObject == barProxy.gameObject,
            "registered nested ray dispatch reaches the exact outboard common scrollbar");
        var localOriginalBefore = visitor.Bar.value;
        var down = new PointerEventData(EventSystem.current) { pointerId = -101, button = PointerEventData.InputButton.Left };
        down.pointerPressRaycast = barHit;
        down.position = visitor.Camera.WorldToScreenPoint(barWorld);
        pointer.Press(down.position);
        pointer.Drag(visitor.Camera.WorldToScreenPoint(barProxy.CoordinateRoot.TransformPoint(new Vector3(0, -65, 0))));
        // Release uses the real metadata input proxy rather than an inert clone callback.
        proxy = barProxy; pointer.Release(down.position);
        visitor.Root.sizeDelta = new Vector2(600, 400);
        Check(Near(visitor.Bar.value, localOriginalBefore), "common scrollbar press/drag never changes the hidden private native client control");
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Select(); visitor.Core.Tick(Time.unscaledTime); visitor.Deliver(host);
        Check(host.Bar.value < .5f, "native source scrollbar receives projected original down/drag/up with correct direction and handle geometry");
        host.Publish(); host.Deliver(visitor);
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint();
        Check(Near(PicturePosition(visitor, picture.transform), host.Scroll.verticalNormalizedPosition),
            "guest scrollbar acceptance reaches its own visible picture through the small author state");

        // Genuine native raycast disablement wins over a previously admitted packet.
        host.Select(); var blocker = host.Root.GetComponent<CanvasGroup>(); blocker.blocksRaycasts = false;
        float blockedBefore = host.Scroll.verticalNormalizedPosition;
        host.Core.Receive(2, wheel, wheel.Length); // a duplicate remains a no-op too
        Check(SharedWindowScrollBridge.Wheel(host.Scroll, new Vector2(0, -4)), "shared disabled input remains consumed at its original owner");
        Check(Near(host.Scroll.verticalNormalizedPosition, blockedBefore), "native canvas-group permission is retained at execution time");
        host.Publish(); host.Deliver(visitor); visitor.Select(); visitor.Core.AttachResultsInputs(picture.transform, visitor.Canvas);
        Check(picture.GetComponentsInChildren<SharedWindowScrollProxy>(true).All(x => !x.gameObject.activeInHierarchy),
            "genuine source canvas-group denial disables every own observer input surface");
        blocker.blocksRaycasts = true; host.Publish(); host.Deliver(visitor); visitor.Select();
        visitor.Core.AttachResultsInputs(picture.transform, visitor.Canvas);
        Check(picture.GetComponentsInChildren<SharedWindowScrollProxy>(true).Any(x => x.gameObject.activeInHierarchy),
            "re-enabled original permission immediately restores common viewport input");
        host.Select(); host.Raycaster.enabled = false; host.Publish();
        SharedWindowScrollMessage disabled = FirstState(host);
        Check((disabled.Flags & 1) == 0, "a disabled original converted raycaster publishes an ineligible Results descriptor");
        host.Deliver(visitor); visitor.Select(); visitor.Core.AttachResultsInputs(picture.transform, visitor.Canvas);
        Check(picture.GetComponentsInChildren<SharedWindowScrollProxy>(true).All(x => !x.gameObject.activeInHierarchy)
            && picture.GetComponentsInChildren<SharedResultsHeaderGraphic>(true).All(x => !x.gameObject.activeInHierarchy),
            "native raycaster denial retires viewport scrollbar and header input together");
        host.Select(); host.Raycaster.enabled = true; host.Publish(); host.Deliver(visitor); visitor.Select();
        visitor.Core.AttachResultsInputs(picture.transform, visitor.Canvas);
        int proxyCount = picture.GetComponentsInChildren<SharedWindowScrollProxy>(true).Length;
        int notes = GloomhavenVR.Core.VRLog.Notes.Count, warnings = GloomhavenVR.Core.VRLog.Warnings.Count;
        for (int i = 0; i < 32; i++) visitor.Core.AttachResultsInputs(picture.transform, visitor.Canvas);
        Check(picture.GetComponentsInChildren<SharedWindowScrollProxy>(true).Length == proxyCount,
            "unchanged frame input geometry is reused without rebuilding or duplicating observer controls");
        Check(GloomhavenVR.Core.VRLog.Notes.Count == notes && GloomhavenVR.Core.VRLog.Warnings.Count == warnings,
            "unchanged Results input state does not repeat normal lifecycle or anomaly reports");

        IEnumerator wholeWindow = WholeWindowInputs(host, visitor, picture.transform, pointer, stick, hand);
        while (wholeWindow.MoveNext()) yield return wholeWindow.Current;

        IEnumerator concurrent = ConcurrentInputs(host, visitor, third, picture.transform);
        while (concurrent.MoveNext()) yield return concurrent.Current;

        // Native show/hide in one frame starts a new input opening.
        SharedWindowSpawnAuthority.AuthorId = 1; host.Select(); host.Core.ObserveVisibility(host.Windows[7]!, false); host.Publish();
        SharedWindowScrollMessage reopened = FirstState(host);
        Check(reopened.Opening != opening.Opening, "same-frame native hide/show rejects preceding-opening input");
        UIWindow identity = host.Windows[7]!; host.Windows[7] = null;
        host.Core.ObserveVisibility(identity, false); host.Windows[7] = identity; host.Publish();
        SharedWindowScrollMessage afterHandoff = FirstState(host);
        Check(afterHandoff.Opening != reopened.Opening, "captured exact native subject survives shared-kind retirement before Hide notification");
        float reopenedBefore = host.Scroll.verticalNormalizedPosition;
        Receive(host, 2, new SharedWindowScrollMessage { Operation = SharedScrollOperation.Wheel, Kind = 7, Control = 1,
            Author = 1, Requester = 2, Epoch = opening.Epoch, Opening = opening.Opening, Generation = opening.Generation,
            Content = opening.Content, Path = opening.Path, Sequence = 10000, Y = -10 });
        Check(Near(reopenedBefore, host.Scroll.verticalNormalizedPosition), "old opening cannot scroll a later native Victory window");

        host.Select(); host.Sent.Clear(); host.Scroll.verticalNormalizedPosition = .99f; host.Publish(); SharedWindowScrollMessage priorControl = FirstState(host);
        RectTransform originalContent = host.Scroll.content, originalViewport = host.Scroll.viewport;
        GameObject sameObject = host.Scroll.gameObject; Object.DestroyImmediate(host.Scroll);
        host.Scroll = sameObject.AddComponent<ScrollRect>(); host.Scroll.content = originalContent;
        host.Scroll.viewport = originalViewport; host.Scroll.vertical = true; host.Scroll.horizontal = false;
        host.Scroll.verticalScrollbar = host.Bar; host.Scroll.movementType = ScrollRect.MovementType.Clamped;
        host.Scroll.verticalNormalizedPosition = 1; host.Publish(); SharedWindowScrollMessage replacement = FirstState(host);
        Check(replacement.Generation != priorControl.Generation, "same-path native component replacement receives a new exact generation");
        float replacementBefore = host.Scroll.verticalNormalizedPosition;
        Receive(host, 3, new SharedWindowScrollMessage { Operation = SharedScrollOperation.Wheel, Kind = 7, Control = 1,
            Author = 1, Requester = 3, Epoch = priorControl.Epoch, Opening = priorControl.Opening, Generation = priorControl.Generation,
            Content = priorControl.Content, Path = priorControl.Path, Sequence = 10001, Y = -10 });
        Check(Near(replacementBefore, host.Scroll.verticalNormalizedPosition), "old component generation cannot mutate its same-path pooled replacement");

        // Unrelated windows and private composed-host controls remain untouched.
        host.Select(); var privateRoot = Rect("Private unrelated window", null, new Vector2(200, 200));
        var privateScroll = privateRoot.gameObject.AddComponent<ScrollRect>();
        Check(!SharedWindowScrollBridge.Wheel(privateScroll, Vector2.down), "ordinary private windows keep their original local wheel path");
        var mapHost = new Peer(4, 2); var privateMapScroll = mapHost.Root.gameObject.AddComponent<ScrollRect>();
        SharedWindowSpawnAuthority.AuthorId = 4; mapHost.Select(); mapHost.Publish();
        Check(!SharedWindowScrollBridge.Wheel(privateMapScroll, Vector2.down), "private loadout sibling outside the exact public story subtree stays local");

        // A2D host must never be the scroll author for somebody else's3D map.
        mapHost.Map = false; var map2 = new Peer(5, 2); var map3 = new Peer(6, 2);
        host.Map = visitor.Map = third.Map = false;
        map2.Publish(); Check(map2.Sent.Any(x => SharedWindowScrollCodec.TryRead(x.Data, x.Data.Length, out var m) && m.Kind == 2 && m.Author == 5),
            "map-off host yields only public map scroll authority to the smallest participating VR peer");
        map2.Deliver(map3); map3.Select(); Check(SharedWindowScrollBridge.Wheel(map3.Scroll, Vector2.down), "second3D visitor can operate with a2D native host");
        Check(map3.Sent.All(x => !x.HostOnly), "VR surrogate inputs reach Others instead of the private2D host");
        float mapBefore = map2.Scroll.verticalNormalizedPosition; map3.Deliver(map2);
        Check(map2.Scroll.verticalNormalizedPosition < mapBefore, "participating VR surrogate executes the original map scroll");
        Check(Near(mapHost.Scroll.verticalNormalizedPosition, 1), "private2D host content is never mutated by common3D scroll");
        Unmodded.Add(4); SharedWindowSpawnAuthority.AuthorId = 5; map2.Publish();
        Check(map2.Sent.Count > 0, "unmodded-host surrogate still emits the public map scroll heartbeat");

        host.Select();
        var inner = Rect("Native inner button", host.Bar.transform, new Vector2(22, 22));
        inner.gameObject.AddComponent<Image>(); var actualButton = inner.gameObject.AddComponent<InnerScrollButton>();
        Canvas.ForceUpdateCanvases(); host.Camera.Render();
        var buttonData = new PointerEventData(EventSystem.current) { position = host.Camera.WorldToScreenPoint(inner.position) };
        var buttonHits = new List<RaycastResult>(); host.Raycaster.Raycast(buttonData, buttonHits);
        Check(buttonHits.Any(hit => hit.gameObject == inner.gameObject), "native inner scrollbar button is visibly raycastable");
        var nativePointer = new UguiPointer(); nativePointer.SetFixtureHover(inner.gameObject, buttonHits.First(hit => hit.gameObject == inner.gameObject));
        nativePointer.Press(buttonData.position);
        Check(actualButton.DownCalls == 1, "a nearer original pointer-down handler retains priority over an ancestor shared scrollbar");
        Check(GloomhavenVR.Core.VRLog.Notes.Any(x => x.StartsWith("RESULTS SCROLL SOURCE:") && x.Contains("controls=2, eligible=2"))
            && GloomhavenVR.Core.VRLog.Notes.Any(x => x.StartsWith("RESULTS SCROLL INPUT:") && x.Contains("descriptors=2, eligible=2, resolved=2")),
            "normal lifecycle evidence separates original eligible controls from resolved observer descriptors");
        visitor.Select(); visitor.Core.Reset();
        Check(picture.GetComponentsInChildren<SharedResultsHeaderGraphic>(true).All(x => !x.gameObject.activeSelf),
            "reset retires header hover with the existing Results input proxies");
        Object.DestroyImmediate(picture);
        Check(UguiPokeSurfaces.NestedOf(visitor.Canvas) == null, "retiring the complete picture releases its exact nested input registration");
        FFSNetwork.IsOnline = false; host.Select();
        Check(!SharedWindowScrollBridge.Wheel(host.Scroll, Vector2.down), "offline returns to native local scrolling");
        foreach (Peer p in Peers.Values) { p.Core.Reset(); Object.DestroyImmediate(p.Camera.targetTexture); }
        report(_checks);
    }
}
