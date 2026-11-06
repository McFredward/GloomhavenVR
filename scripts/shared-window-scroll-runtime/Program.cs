using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.Net;
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
            var holder = Rect("Public scroll", Root, new Vector2(400, 200));
            var viewport = Rect("Viewport", holder, new Vector2(400, 200)); viewport.gameObject.AddComponent<Image>();
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport, new Vector2(400, height));
            content.pivot = new Vector2(.5f, 1); content.anchorMin = content.anchorMax = new Vector2(.5f, 1);
            var ink = Rect("Visible source row", content, new Vector2(350, 80)); ink.gameObject.AddComponent<Image>();
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
    private static void Receive(Peer peer, int sender, SharedWindowScrollMessage message)
    { peer.Select(); byte[] bytes = SharedWindowScrollCodec.Write(message); Check(peer.Core.Receive(sender, bytes, bytes.Length), "valid control routes without native network dependency"); }

    internal static IEnumerator Run(Action<int> report)
    {
        _checks = 0; _clock = 0; Peers.Clear(); Unmodded.Clear(); FFSNetwork.IsOnline = true;
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
        GameObject picture = Object.Instantiate(host.Root.gameObject, quarantine.transform, false);
        foreach (ScrollRect scroll in picture.GetComponentsInChildren<ScrollRect>(true)) Object.DestroyImmediate(scroll);
        foreach (Scrollbar bar in picture.GetComponentsInChildren<Scrollbar>(true)) Object.DestroyImmediate(bar);
        foreach (UIWindow window in picture.GetComponentsInChildren<UIWindow>(true)) Object.DestroyImmediate(window);
        foreach (GraphicRaycaster raycaster in picture.GetComponentsInChildren<GraphicRaycaster>(true)) Object.DestroyImmediate(raycaster);
        foreach (Graphic graphic in picture.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        var group = picture.AddComponent<CanvasGroup>(); group.blocksRaycasts = group.interactable = false;
        var mount = Rect("GloomhavenVR.Results.Observer", visitor.Root, new Vector2(600, 400));
        picture.transform.SetParent(mount, false); ((RectTransform)picture.transform).anchoredPosition3D = Vector3.zero;
        picture.GetComponent<Canvas>().worldCamera = visitor.Camera;
        visitor.Select(); visitor.Core.AttachResultsInputs(picture.transform, visitor.Canvas);
        SharedWindowScrollProxy proxy = picture.GetComponentsInChildren<SharedWindowScrollProxy>(true).First(x => x.Control == 1);
        Check(picture.GetComponentsInChildren<ScrollRect>(true).Length == 0, "common picture never reinstates cloned native scroll controllers");
        Check(UguiPokeSurfaces.NestedOf(visitor.Canvas)?.Contains(picture.GetComponent<Canvas>()) == true,
            "only the exact common picture input canvas joins the existing nested-pointer census");
        Canvas.ForceUpdateCanvases(); yield return null; visitor.Camera.Render();
        var inputRaycaster = proxy.GetComponentInParent<GraphicRaycaster>();
        var data = new PointerEventData(EventSystem.current) { position = visitor.Camera.WorldToScreenPoint(proxy.transform.position) };
        var hits = new List<RaycastResult>(); inputRaycaster.Raycast(data, hits);
        Check(hits.Any(x => x.gameObject == proxy.gameObject), "real original GraphicRaycaster reaches the common viewport through inert observer groups");
        pointer.SetFixtureHover(proxy.gameObject); pointer.Scroll(new Vector2(0, -2));
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Select(); visitor.Core.Tick(Time.unscaledTime); Check(visitor.Sent.Count > 0, "visible common viewport wheel creates its exact original-address request");
        visitor.Deliver(host); Check(host.Scroll.verticalNormalizedPosition < after, "common source-sized viewport forwards native wheel despite private client row differences");

        host.Publish(); host.Deliver(visitor); until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Paint(); visitor.Core.AttachResultsInputs(picture.transform, visitor.Canvas);
        SharedWindowScrollProxy barProxy = picture.GetComponentsInChildren<SharedWindowScrollProxy>(true).First(x => x.Control == 2);
        var localOriginalBefore = visitor.Bar.value;
        var down = new PointerEventData(EventSystem.current) { pointerId = -101, button = PointerEventData.InputButton.Left };
        var barCaster = barProxy.GetComponentInParent<GraphicRaycaster>();
        down.pointerPressRaycast = new RaycastResult { module = barCaster, gameObject = barProxy.gameObject };
        down.position = visitor.Camera.WorldToScreenPoint(barProxy.CoordinateRoot!.TransformPoint(new Vector3(0, 50, 0)));
        pointer.SetFixtureHover(barProxy.gameObject, down.pointerPressRaycast); pointer.Press(down.position);
        pointer.Drag(visitor.Camera.WorldToScreenPoint(barProxy.CoordinateRoot.TransformPoint(new Vector3(0, -65, 0))));
        // Release uses the real metadata input proxy rather than an inert clone callback.
        proxy = barProxy; proxy.OnPointerUp(down);
        Check(Near(visitor.Bar.value, localOriginalBefore), "common scrollbar press/drag never changes the hidden private native client control");
        until = Time.unscaledTime + .08f; while (Time.unscaledTime < until) yield return null;
        visitor.Select(); visitor.Core.Tick(Time.unscaledTime); visitor.Deliver(host);
        Check(host.Bar.value < .5f, "native source scrollbar receives projected original down/drag/up with correct direction and handle geometry");

        // Genuine native raycast disablement wins over a previously admitted packet.
        host.Select(); var blocker = host.Root.gameObject.AddComponent<CanvasGroup>(); blocker.blocksRaycasts = false;
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
        int proxyCount = picture.GetComponentsInChildren<SharedWindowScrollProxy>(true).Length;
        for (int i = 0; i < 32; i++) visitor.Core.AttachResultsInputs(picture.transform, visitor.Canvas);
        Check(picture.GetComponentsInChildren<SharedWindowScrollProxy>(true).Length == proxyCount,
            "unchanged frame input geometry is reused without rebuilding or duplicating observer controls");

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
        Object.DestroyImmediate(picture);
        Check(UguiPokeSurfaces.NestedOf(visitor.Canvas) == null, "retiring the complete picture releases its exact nested input registration");
        FFSNetwork.IsOnline = false; host.Select();
        Check(!SharedWindowScrollBridge.Wheel(host.Scroll, Vector2.down), "offline returns to native local scrolling");
        foreach (Peer p in Peers.Values) { p.Core.Reset(); Object.DestroyImmediate(p.Camera.targetTexture); }
        report(_checks);
    }
}
