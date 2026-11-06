using System;
using System.Collections.Generic;
using FFSNet;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>One active session owner; instance coordinators keep transport/lifecycle state isolated.</summary>
internal static class SharedWindowScrollSync
{
    private static readonly SharedWindowScrollCoordinator Active = new();
    internal static void Bind(INetTransport transport, Func<byte[], int, bool, bool> sendReliable) => Active.Bind(transport, sendReliable);
    internal static void Tick(float now) => Active.Tick(now);
    internal static void LateTick(float now) => Active.LateTick(now);
    internal static void ObserveWindowVisibility(UIWindow window, bool shown) => Active.ObserveVisibility(window, shown);
    internal static bool Receive(int sender, byte[] bytes, int length) => Active.Receive(sender, bytes, length);
    internal static void ForgetPeer(int peer) => Active.ForgetPeer(peer);
    internal static void Reset() => Active.Reset();
    internal static void AttachResultsInputs(Transform root) => Active.AttachResultsInputs(root);
}

/// <summary>One reliable input coordinator, like the public merchant cabinet. Every visitor
/// addresses an original public ScrollRect/Scrollbar; only the common author executes input.
/// Received values never discover a callback by name or invoke gameplay controllers.</summary>
internal sealed class SharedWindowScrollCoordinator
{
    private sealed class Window
    {
        internal Transform? Root;
        internal uint Opening, Revision;
        internal ulong Content;
        internal bool Visible;
    }
    private sealed class Target
    {
        internal Component? Original;
        internal SharedWindowScrollMessage State = null!;
        internal float NextSend;
        internal SharedWindowScrollMessage? LastSent;
        internal PointerEventData? Pointer;
        internal int PointerOwner, PointerId;
        internal bool Dragging;
        internal float FromX, FromY, ReceivedAt, Duration;
        internal bool HasApplied;
    }
    private INetTransport? _transport;
    private Func<byte[], int, bool, bool>? _send;
    private readonly Window[] Windows = CreateWindows();
    private readonly Dictionary<string, Target> Sources = new();
    private readonly Dictionary<string, Target> Observed = new();
    private readonly Dictionary<int, uint> LastRequest = new();
    private readonly Queue<SharedWindowScrollMessage> Outgoing = new();
    private readonly List<ScrollRect> Scrolls = new();
    private readonly List<Scrollbar> Bars = new();
    private readonly List<string> Retired = new();
    private readonly HashSet<string> Present = new();
    private int _author;
    private uint _epoch = NewEpoch(), _opening, _generation, _revision, _request;
    private float _nextCapture, _nextWarning, _nextRequestSend;
    private SharedWindowScrollMessage? _tail;
    private readonly Vector3[] Corners = new Vector3[4];
    private readonly SharedWindowScrollMessage?[] Closed = new SharedWindowScrollMessage?[8];
    private readonly Dictionary<string, SharedWindowScrollProxy> Proxies = new();
    private Transform? _proxyRoot;
    private uint _proxyVersion, _paintedProxyVersion;
    private static Window[] CreateWindows()
    { var values = new Window[8]; for (int i = 0; i < values.Length; i++) values[i] = new Window(); return values; }
    private static uint NewEpoch() { uint x = unchecked((uint)(DateTime.UtcNow.Ticks ^ Environment.TickCount)); return x == 0 ? 1u : x; }
    private static uint Next(ref uint value) { if (++value == 0) ++value; return value; }
    private int Local => _transport?.LocalPlayerId ?? 0;
    private bool Online => _transport?.IsOnline == true && !NetSession.FlatNetMode
        && FFSNetwork.IsOnline && Local > 0;
    private bool IsAuthor => Online && _author == Local;
    private static string Key(byte kind, byte control, string path) => kind + ":" + control + ":" + path;

    internal void Bind(INetTransport transport, Func<byte[], int, bool, bool> sendReliable)
    {
        if (!ReferenceEquals(_transport, transport)) Reset();
        _transport = transport; _send = sendReliable;
        SharedWindowScrollBridge.Submit = Input;
    }
    internal void ObserveVisibility(UIWindow window, bool shown)
    {
        if (!Online || !IsAuthor || shown) return;
        SharedWindowKind kind = SharedWindows.KindOf(window);
        if (kind != SharedWindowKind.None) Windows[(byte)kind].Root = null;
    }
    internal void Reset()
    {
        foreach (Target target in Sources.Values) EndGesture(target);
        Sources.Clear(); Observed.Clear(); LastRequest.Clear(); Outgoing.Clear();
        foreach (Window window in Windows) { window.Root = null; window.Opening = 0; window.Visible = false; window.Revision = 0; }
        _author = 0; _epoch = NewEpoch(); _nextCapture = _nextWarning = _nextRequestSend = 0;
        _tail = null; Array.Clear(Closed, 0, Closed.Length);
        foreach (SharedWindowScrollProxy proxy in Proxies.Values) if (proxy != null) proxy.gameObject.SetActive(false);
        Proxies.Clear(); _proxyRoot = null; _proxyVersion = _paintedProxyVersion = 0;
        SharedWindowScrollBridge.Submit = null;
        // Bindings survive an offline/flat-net reset; the same transport can resume.
        // Request/opening nonces stay monotonic across local module recreation.
    }
    private void ObserveAuthor()
    {
        int next = SharedWindowSpawnAuthority.AuthorId;
        if (_author == next) return;
        Reset(); _author = next;
    }
    internal void Tick(float now)
    {
        if (!Online) return;
        ObserveAuthor(); SharedWindowScrollBridge.Submit = Input; Flush();
        if (!IsAuthor) ApplyObserved();
    }
    internal void LateTick(float now)
    {
        if (!Online) return;
        ObserveAuthor();
        if (!IsAuthor) { ApplyObserved(); return; }
        if (now < _nextCapture) return;
        _nextCapture = now + 1f / 15f;
        Capture(now);
    }

    private void Capture(float now)
    {
        Present.Clear();
        for (int i = 1; i < Closed.Length; i++)
            if (Closed[i] is SharedWindowScrollMessage closure && Send(closure)) Closed[i] = null;
        for (byte value = 1; value <= 7; value++)
        {
            var kind = (SharedWindowKind)value; Window window = Windows[value];
            Transform? root = SharedWindows.ParticipatesHere(kind) ? SharedWindowScrollBridge.PublicRoot(kind, out _) : null;
            bool visible = root != null && root.gameObject.activeInHierarchy;
            if (!visible)
            {
                if (window.Visible)
                {
                    var closed = new SharedWindowScrollMessage { Operation = SharedScrollOperation.Closed,
                        Kind = value, Author = Local, Epoch = _epoch, Opening = window.Opening, Revision = Next(ref _revision) };
                    if (!Send(closed)) { Closed[value] = closed; Warn(); }
                }
                window.Visible = false; window.Root = null; continue;
            }
            ulong content = Content(kind);
            if (!window.Visible || window.Root != root || window.Content != content)
            { window.Visible = true; window.Root = root; window.Content = content; window.Opening = Next(ref _opening); }
            root!.GetComponentsInChildren(true, Scrolls);
            root.GetComponentsInChildren(true, Bars);
            // Bound native discovery to the seven admitted PUBLIC roots. Inactive
            // native tabs can retain a scroll position, but cannot receive input.
            for (int i = 0; i < Scrolls.Count && i < 32; i++) CaptureTarget(value, window, Scrolls[i], 1, now);
            for (int i = 0; i < Bars.Count && i < 32; i++) CaptureTarget(value, window, Bars[i], 2, now);
        }
        Retired.Clear();
        foreach (var pair in Sources) if (!Present.Contains(pair.Key)) Retired.Add(pair.Key);
        foreach (string key in Retired)
        {
            Target old = Sources[key]; EndGesture(old);
            var retired = CopyIdentity(old.State, SharedScrollOperation.State);
            retired.Revision = Next(ref _revision); retired.Viewport = old.State.Viewport;
            if (!Send(retired)) Warn();
            Sources.Remove(key);
        }
    }
    private void CaptureTarget(byte kind, Window window, Component component, byte control, float now)
    {
        // Mod-owned result input proxies contain no native ScrollRect/Scrollbar.
        if (!component.transform.IsChildOf(window.Root)) return;
        string path = SharedWindowScrollBridge.Path(window.Root!, component.transform);
        string key = Key(kind, control, path); Present.Add(key);
        if (Sources.Count >= 128 && !Sources.ContainsKey(key)) return;
        if (!Sources.TryGetValue(key, out Target? target) || target.Original != component
            || target.State.Opening != window.Opening)
        {
            if (target != null) EndGesture(target);
            target = new Target { Original = component, State = new SharedWindowScrollMessage {
                Kind = kind, Control = control, Path = path, Epoch = _epoch, Opening = window.Opening,
                Generation = Next(ref _generation), Author = Local, Content = window.Content } };
            Sources[key] = target;
        }
        SharedWindowScrollMessage next = Snapshot(target.State, component);
        if (!SharedWindowScrollCodec.Valid(next)) return;
        bool changed = target.LastSent == null || !Same(target.LastSent, next);
        target.State = next;
        if (!changed && now < target.NextSend) return;
        if (Send(next)) { target.LastSent = next; target.NextSend = now + .5f; }
        else Warn(); // Exact state remains eligible; no successful local receipt is invented.
    }
    private SharedWindowScrollMessage Snapshot(SharedWindowScrollMessage identity, Component component)
    {
        var state = CopyIdentity(identity, SharedScrollOperation.State); state.Revision = Next(ref _revision);
        if (component is ScrollRect scroll)
        {
            Vector2 p = scroll.normalizedPosition; state.X = p.x; state.Y = p.y;
            state.Flags = (byte)((scroll.isActiveAndEnabled && scroll.gameObject.activeInHierarchy && scroll.content != null ? 1 : 0)
                | (scroll.horizontal ? 2 : 0) | (scroll.vertical ? 4 : 0));
            RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            state.Viewport = SharedWindowScrollBridge.Path(Windows[state.Kind].Root!, viewport);
            if (scroll.content != null)
            {
                scroll.content.GetWorldCorners(Corners);
                Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, 0), max = new(float.NegativeInfinity, float.NegativeInfinity, 0);
                for (int i = 0; i < 4; i++) { Vector3 point = viewport.InverseTransformPoint(Corners[i]); min = Vector3.Min(min, point); max = Vector3.Max(max, point); }
                state.RangeX = Mathf.Max(0, max.x - min.x - viewport.rect.width);
                state.RangeY = Mathf.Max(0, max.y - min.y - viewport.rect.height);
            }
        }
        else if (component is Scrollbar bar)
        {
            state.X = bar.value; state.HandleSize = bar.size; state.Direction = (byte)bar.direction;
            state.Flags = (byte)(bar.isActiveAndEnabled && bar.IsInteractable() ? 1 : 0);
            state.Viewport = state.Path;
        }
        return state;
    }
    private static bool Same(SharedWindowScrollMessage a, SharedWindowScrollMessage b) => a.X == b.X && a.Y == b.Y
        && a.RangeX == b.RangeX && a.RangeY == b.RangeY && a.Flags == b.Flags && a.Viewport == b.Viewport
        && a.HandleSize == b.HandleSize && a.Direction == b.Direction;
    private static SharedWindowScrollMessage CopyIdentity(SharedWindowScrollMessage m, SharedScrollOperation operation) => new() {
        Operation = operation, Kind = m.Kind, Control = m.Control, Path = m.Path, Author = m.Author,
        Epoch = m.Epoch, Opening = m.Opening, Generation = m.Generation, Content = m.Content };

    private bool Input(Component component, SharedScrollInput operation, Vector2 value, int pointer)
    {
        if (!Online) return false;
        ObserveAuthor();
        Target? target = null;
        if (component is SharedWindowScrollProxy proxy)
        {
            if (SharedWindows.ParticipatesHere(proxy.Kind))
                Observed.TryGetValue(Key((byte)proxy.Kind, proxy.Control, proxy.Address), out target);
        }
        else
        {
            var targets = IsAuthor ? Sources : Observed;
            foreach (Target candidate in targets.Values)
            {
                if (!IsAuthor) candidate.Original = Resolve(candidate.State);
                if (candidate.Original == component) { target = candidate; break; }
            }
        }
        if (target == null)
        {
            if (IsAuthor)
            {
                Capture(Time.unscaledTime);
                foreach (Target candidate in Sources.Values) if (candidate.Original == component) { target = candidate; break; }
            }
            if (target == null) return IsPublicInput(component);
        }
        if (!SharedWindows.ParticipatesHere((SharedWindowKind)target.State.Kind)) return false;
        var request = CopyIdentity(target.State, (SharedScrollOperation)operation);
        request.Requester = Local; request.Sequence = Next(ref _request); request.Pointer = pointer;
        request.X = value.x; request.Y = value.y;
        if (!SharedWindowScrollCodec.Valid(request)) return true;
        if (IsAuthor) { Execute(target, request); _nextCapture = 0; return true; }
        if (Outgoing.Count >= 128) { Warn(); return true; }
        bool continuous = operation == SharedScrollInput.Wheel || operation == SharedScrollInput.Position || operation == SharedScrollInput.PointerDrag;
        bool merge = continuous && _tail != null && _tail.Operation == request.Operation && _tail.Kind == request.Kind
            && _tail.Control == request.Control && _tail.Path == request.Path && _tail.Opening == request.Opening
            && _tail.Generation == request.Generation && _tail.Pointer == request.Pointer;
        if (merge && operation == SharedScrollInput.Wheel && Math.Abs(_tail!.X + request.X) <= 256 && Math.Abs(_tail.Y + request.Y) <= 256)
        { _tail.X += request.X; _tail.Y += request.Y; _tail.Sequence = request.Sequence; }
        else if (merge && operation != SharedScrollInput.Wheel)
        { _tail!.X = request.X; _tail.Y = request.Y; _tail.Sequence = request.Sequence; }
        else { Outgoing.Enqueue(request); _tail = request; }
        if (!continuous) _nextRequestSend = 0;
        Flush();
        // Consume BEFORE local native mutation; the common original alone owns it.
        return true;
    }
    private static bool IsPublicInput(Component component)
    {
        if (component is SharedWindowScrollProxy proxy) return SharedWindows.ParticipatesHere(proxy.Kind);
        if (!(component is ScrollRect) && !(component is Scrollbar)) return false;
        for (byte i = 1; i <= 7; i++)
        {
            var kind = (SharedWindowKind)i;
            if (!SharedWindows.ParticipatesHere(kind)) continue;
            Transform? root = SharedWindowScrollBridge.PublicRoot(kind, out _);
            if (root != null && component.transform.IsChildOf(root)) return true;
        }
        return false;
    }
    private static ulong Content(SharedWindowKind kind)
    {
        if (kind == SharedWindowKind.ScenarioStory || kind == SharedWindowKind.MapStory)
        {
            UICharacterStoryBox? box = kind == SharedWindowKind.ScenarioStory
                ? Singleton<StoryController>.IsInitialized ? Singleton<StoryController>.Instance.DialogBox : null
                : Singleton<MapStoryController>.IsInitialized ? Singleton<MapStoryController>.Instance.dialogBox : null;
            return box == null ? 0 : ((ulong)RemoteStorySync.HashDialog(box.dialogs) << 32) | unchecked((uint)(box.currentDialogIndex + 1));
        }
        if (kind == SharedWindowKind.Encounter)
        {
            if (WorldUI.MapRoom.MapCityEventReadyUp.Active) return WorldUI.MapRoom.MapCityEventReadyUp.ContentKey;
            UIEventPanel? panel = Singleton<UIEventPanel>.IsInitialized ? Singleton<UIEventPanel>.Instance : null;
            return panel == null ? 0 : ((ulong)NetProtocol.HashMapKey(panel.eventData?.ID ?? string.Empty) << 32)
                | NetProtocol.HashMapKey(panel.currentScreen?.Name ?? string.Empty);
        }
        if (kind == SharedWindowKind.QuestConfirm)
        {
            UIQuestPopup? popup = SharedWindows.WindowOf(kind)?.GetComponent<UIQuestPopup>();
            return popup?.quest == null ? 0 : NetProtocol.HashMapKey(popup.quest.LocalisedNameKey);
        }
        return 0;
    }

    private static Component? Resolve(SharedWindowScrollMessage state)
    {
        if (!SharedWindows.ParticipatesHere((SharedWindowKind)state.Kind)) return null;
        if (Content((SharedWindowKind)state.Kind) != state.Content) return null;
        Transform? root = SharedWindowScrollBridge.PublicRoot((SharedWindowKind)state.Kind, out _);
        Transform? target = root == null ? null : SharedWindowScrollBridge.Find(root, state.Path);
        return state.Control == 1 ? target?.GetComponent<ScrollRect>() : target?.GetComponent<Scrollbar>();
    }
    private void Flush()
    {
        if (Time.unscaledTime < _nextRequestSend) return;
        _nextRequestSend = Time.unscaledTime + 1f / 15f;
        for (int i = 0; i < 8 && Outgoing.Count > 0; i++)
        {
            SharedWindowScrollMessage request = Outgoing.Peek();
            if (request.Author != _author) { Outgoing.Dequeue(); continue; }
            if (!Send(request)) { Warn(); return; }
            Outgoing.Dequeue(); if (ReferenceEquals(_tail, request)) _tail = null;
        }
    }
    private bool Send(SharedWindowScrollMessage message)
    { byte[] bytes = SharedWindowScrollCodec.Write(message); return _send?.Invoke(bytes, bytes.Length, message.Operation != SharedScrollOperation.State
        && message.Operation != SharedScrollOperation.Closed && _author == PlayerRegistry.HostPlayerID) == true; }

    internal bool Receive(int sender, byte[] bytes, int length)
    {
        if (!SharedWindowScrollCodec.TryRead(bytes, length, out SharedWindowScrollMessage message)) return false;
        if (!Online || sender <= 0 || sender == Local || VersionGuard.PeerBuild(sender) != NetProtocol.ModBuild) return true;
        ObserveAuthor();
        if (message.Author != _author || !SharedWindows.ParticipatesHere((SharedWindowKind)message.Kind)) return true;
        bool state = message.Operation == SharedScrollOperation.State || message.Operation == SharedScrollOperation.Closed;
        if (state)
        {
            if (IsAuthor || sender != _author) return true;
            Window window = Windows[message.Kind];
            // ReliableOrdered retains the author's emission order. New epochs only
            // arrive from the currently elected compatible author, never a requester.
            if (_epoch != message.Epoch) { Observed.Clear(); foreach (Window w in Windows) { w.Opening = 0; w.Revision = 0; } _epoch = message.Epoch; }
            if (window.Revision != 0 && !SharedWindowScrollCodec.Newer(message.Revision, window.Revision)) return true;
            window.Revision = message.Revision;
            if (window.Opening != 0 && message.Opening != window.Opening
                && !SharedWindowScrollCodec.Newer(message.Opening, window.Opening)) return true;
            if (message.Opening != window.Opening || message.Operation == SharedScrollOperation.Closed)
            {
                Retired.Clear(); foreach (var pair in Observed) if (pair.Value.State.Kind == message.Kind) Retired.Add(pair.Key);
                foreach (string retired in Retired) Observed.Remove(retired);
                window.Opening = message.Opening;
            }
            window.Visible = message.Operation != SharedScrollOperation.Closed;
            if (!window.Visible) { ++_proxyVersion; return true; }
            string key = Key(message.Kind, message.Control, message.Path);
            if ((message.Flags & 1) == 0) { if (Observed.Remove(key)) ++_proxyVersion; return true; }
            if (Observed.TryGetValue(key, out Target? target)
                && !SharedWindowScrollCodec.Newer(message.Revision, target.State.Revision)) return true;
            if (target == null && Observed.Count >= 128) return true;
            if (target == null || target.State.Generation != message.Generation)
            { target = new Target { State = message }; ++_proxyVersion; }
            else if (target.State.Viewport != message.Viewport || target.State.Flags != message.Flags
                || target.State.RangeX != message.RangeX || target.State.RangeY != message.RangeY
                || target.State.HandleSize != message.HandleSize || target.State.Direction != message.Direction) ++_proxyVersion;
            if (!ReferenceEquals(target.State, message) && !Same(target.State, message))
            {
                Vector2 current = Display(target);
                target.FromX = current.x; target.FromY = current.y;
                target.ReceivedAt = Time.unscaledTime; target.Duration = 1f / 15f;
            }
            target.State = message; Observed[key] = target;
            return true;
        }
        if (!IsAuthor || message.Requester != sender || message.Epoch != _epoch
            || LastRequest.TryGetValue(sender, out uint sequence) && !SharedWindowScrollCodec.Newer(message.Sequence, sequence)) return true;
        if (!LastRequest.ContainsKey(sender) && LastRequest.Count >= 8) return true;
        LastRequest[sender] = message.Sequence;
        if (Sources.TryGetValue(Key(message.Kind, message.Control, message.Path), out Target? source)
            && source.State.Opening == message.Opening && source.State.Generation == message.Generation && source.State.Content == message.Content)
        { Execute(source, message); _nextCapture = 0; }
        return true;
    }
    private void Execute(Target target, SharedWindowScrollMessage request)
    {
        Component? original = target.Original;
        if (original == null || !original.gameObject.activeInHierarchy
            || Resolve(target.State) != original) return;
        if (request.Operation == SharedScrollOperation.Wheel)
        {
            EndGesture(target);
            ScrollRect? receiver = original as ScrollRect;
            if (original is Scrollbar linked)
                foreach (Target source in Sources.Values)
                    if (source.Original is ScrollRect candidate && (candidate.horizontalScrollbar == linked || candidate.verticalScrollbar == linked)) { receiver = candidate; break; }
            if (receiver != null && receiver.isActiveAndEnabled)
            { var data = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(request.X, request.Y) }; receiver.OnScroll(data); }
            else if (original is Scrollbar bare && bare.IsInteractable())
            {
                float sign = bare.direction == Scrollbar.Direction.BottomToTop || bare.direction == Scrollbar.Direction.LeftToRight ? 1 : -1;
                bare.value = Mathf.Clamp01(bare.value + request.Y * sign * .015f);
            }
            return;
        }
        if (request.Operation == SharedScrollOperation.Position)
        {
            EndGesture(target);
            if (original is Scrollbar bar && bar.isActiveAndEnabled && bar.IsInteractable()) bar.value = Mathf.Clamp01(request.X);
            else if (original is ScrollRect scroll && scroll.isActiveAndEnabled)
            { scroll.StopMovement(); scroll.normalizedPosition = new Vector2(request.X, request.Y); }
            return;
        }
        if (!(original.transform is RectTransform rect)) return;
        if (request.Operation == SharedScrollOperation.PointerDown)
        {
            EndGesture(target);
            target.Pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerId = request.Pointer };
            target.PointerOwner = request.Requester; target.PointerId = request.Pointer;
        }
        if (target.Pointer == null || target.PointerOwner != request.Requester || target.PointerId != request.Pointer) return;
        var raycaster = original.GetComponentInParent<GraphicRaycaster>();
        Camera? camera = raycaster != null ? raycaster.eventCamera : null;
        Vector3 world = rect.TransformPoint(new Vector2(Mathf.LerpUnclamped(rect.rect.xMin, rect.rect.xMax, request.X),
            Mathf.LerpUnclamped(rect.rect.yMin, rect.rect.yMax, request.Y)));
        PointerEventData pointer = target.Pointer; Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, world);
        pointer.delta = screen - pointer.position; pointer.position = screen;
        pointer.pointerPressRaycast = new RaycastResult { module = raycaster, gameObject = original.gameObject, worldPosition = world };
        if (request.Operation == SharedScrollOperation.PointerDown)
        {
            pointer.pressPosition = screen;
            if (original is Scrollbar bar) { bar.OnPointerDown(pointer); bar.OnInitializePotentialDrag(pointer); }
            else if (original is ScrollRect scroll) scroll.OnInitializePotentialDrag(pointer);
        }
        else if (request.Operation == SharedScrollOperation.PointerDrag)
        {
            if (original is Scrollbar bar)
            { if (!target.Dragging) bar.OnBeginDrag(pointer); bar.OnDrag(pointer); }
            else if (original is ScrollRect scroll)
            { if (!target.Dragging) scroll.OnBeginDrag(pointer); scroll.OnDrag(pointer); }
            target.Dragging = true;
        }
        else EndGesture(target);
    }
    private static void EndGesture(Target target)
    {
        if (target.Pointer != null && target.Original != null)
        {
            if (target.Original is Scrollbar bar) bar.OnPointerUp(target.Pointer);
            if (target.Dragging && target.Original is ScrollRect scroll) scroll.OnEndDrag(target.Pointer);
        }
        target.Pointer = null; target.PointerOwner = target.PointerId = 0; target.Dragging = false;
    }
    private static Vector2 Display(Target target)
    {
        float amount = target.Duration <= 0 ? 1 : Mathf.Clamp01((Time.unscaledTime - target.ReceivedAt) / target.Duration);
        return Vector2.LerpUnclamped(new Vector2(target.FromX, target.FromY), new Vector2(target.State.X, target.State.Y), amount);
    }
    private void ApplyObserved()
    {
        foreach (Target target in Observed.Values)
        {
            Component? original = Resolve(target.State);
            if (original == null) { target.Original = null; target.HasApplied = false; continue; }
            if (target.Original != original || !target.HasApplied)
            { target.Original = original; target.Duration = 0; target.HasApplied = true; }
            Vector2 value = Display(target);
            if (original is ScrollRect scroll)
            {
                if (scroll.velocity != Vector2.zero) scroll.StopMovement();
                if (scroll.normalizedPosition != value) scroll.normalizedPosition = value;
            }
            else if (original is Scrollbar bar && bar.value != value.x) bar.SetValueWithoutNotify(value.x);
        }
    }
    internal void ForgetPeer(int peer)
    {
        LastRequest.Remove(peer);
        foreach (Target target in Sources.Values) if (target.PointerOwner == peer) EndGesture(target);
        if (peer == _author) Reset();
    }
    private void Warn()
    {
        if (Time.unscaledTime < _nextWarning) return;
        _nextWarning = Time.unscaledTime + 10f;
        VRLog.Warn("Net", "Shared scroll transport unavailable; the pending public viewport input is retained.");
    }

    /// <summary>Called only after a complete inert Results picture is validated.
    /// Viewports/scrollbar frames come from that common picture, never private client rows.</summary>
    internal void AttachResultsInputs(Transform pictureRoot)
    {
        if (_proxyRoot == pictureRoot && _proxyVersion == _paintedProxyVersion) return;
        if (_proxyRoot != pictureRoot) { Proxies.Clear(); _proxyRoot = pictureRoot; }
        foreach (SharedWindowScrollProxy old in Proxies.Values) if (old != null) old.gameObject.SetActive(false);
        _paintedProxyVersion = _proxyVersion;
        foreach (Target target in Observed.Values)
        {
            SharedWindowScrollMessage state = target.State;
            if (state.Kind != (byte)SharedWindowKind.Results || state.Viewport.Length == 0) continue;
            Transform? viewport = SharedWindowScrollBridge.Find(pictureRoot, state.Viewport);
            Transform? coordinate = SharedWindowScrollBridge.Find(pictureRoot, state.Path);
            if (!(viewport is RectTransform frame) || !(coordinate is RectTransform rect)) continue;
            string key = Key(state.Kind, state.Control, state.Path);
            if (!Proxies.TryGetValue(key, out SharedWindowScrollProxy? proxy) || proxy == null)
            {
                var go = new GameObject("GloomhavenVR.SharedScrollInput", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(SharedWindowScrollProxy));
                var hit = (RectTransform)go.transform; hit.SetParent(frame, false); hit.anchorMin = Vector2.zero;
                hit.anchorMax = Vector2.one; hit.offsetMin = hit.offsetMax = Vector2.zero;
                Image graphic = go.GetComponent<Image>(); graphic.color = Color.clear; graphic.raycastTarget = true;
                go.layer = frame.gameObject.layer; proxy = go.GetComponent<SharedWindowScrollProxy>();
                Proxies[key] = proxy;
            }
            proxy.Kind = SharedWindowKind.Results; proxy.Address = state.Path; proxy.Control = state.Control;
            proxy.CoordinateRoot = rect;
            proxy.CanScroll = (state.Flags & 1) != 0 && (state.Control == 2 ? state.HandleSize < .999f
                : ((state.Flags & 2) != 0 && state.RangeX > .01f || (state.Flags & 4) != 0 && state.RangeY > .01f));
            proxy.gameObject.SetActive((state.Flags & 1) != 0);
        }
    }
}
