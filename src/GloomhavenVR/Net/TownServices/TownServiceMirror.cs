using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Net.TownServices;

internal sealed class TownServiceSessionInfo
{
    internal int Peer;
    internal uint PublicClaim;
    internal byte Service;
    internal uint Session;
    internal ulong Sequence;
    internal float SampleTime, ReceivedTime, LastSeenTime, SessionAge;
    internal bool Active;
    internal Vector3 Position, Scale;
    internal Quaternion Rotation;
    internal ushort[] Modules = Array.Empty<ushort>();
}

/// <summary>
/// Per-visitor original widget mirrors. Register original templates without opening a remote
/// gameplay service; register local modules after native conversion has completed. No callback,
/// model setter, purchase command or game controller runs on these observer copies.
/// </summary>
internal static partial class TownServiceMirror
{
    static TownServiceMirror() { TownServiceDelivery.Completed = SnapshotSent; }
    internal static readonly TownServiceAssets Assets = new();
    private static readonly Dictionary<int, TownServiceSessionInfo> Sessions = new();
    private static readonly Dictionary<int, TownServiceSessionInfo> VisitorSessions = new();
    internal static IReadOnlyDictionary<int, TownServiceSessionInfo> RemoteSessions => VisitorSessions;
    internal static IReadOnlyDictionary<int, TownServiceSessionInfo> PublicSessions => Sessions;
    internal static event Action<string>? PresentationUnavailable;
    internal static Func<int, Transform?>? SharedFrameForRemote { get; set; }
    private static readonly Dictionary<string, GameObject> Templates = new(StringComparer.Ordinal);
    internal static Func<byte, ushort, string, bool>? ResolveTemplate { get; set; }
    // Presentation-only clone preparation: native gameplay controllers remain neutralized.
    // Generated physical bodies need their clone registered for later original silhouette updates.
    internal static Action<string, GameObject>? PrepareInertGeometry { get; set; }
    private static Dictionary<ushort, LocalModule> Local => _local.Modules;
    private static readonly Dictionary<int, Dictionary<ushort, RemoteModule>> Remote = new();
    private static readonly Dictionary<int, Dictionary<ushort, TownServiceFrame>> Pending = new();
    private static readonly Dictionary<int, Dictionary<ushort, TownServiceFrame>> ReceivedBaselines = new();
    private static readonly Dictionary<long, float> RemoteRetry = new();
    private readonly struct ParentLink
    { internal readonly ushort Module; internal readonly uint Binding;
        internal ParentLink(ushort module, uint binding) { Module = module; Binding = binding; } }
    private static readonly Dictionary<Transform, ParentLink> SourceParents = new();
    private static readonly List<CanvasGroup> ParentGroups = new(2);
    private static readonly List<Canvas> ParentCanvases = new(4);
    private static GameObject? _templateHost;
    private static Transform? _sharedFrame { get => _local.SharedFrame; set => _local.SharedFrame = value; }
    private static Transform? _station { get => _local.Station; set => _local.Station = value; }
    private static byte _service { get => _local.Service; set => _local.Service = value; }
    private static uint _session { get => _local.Session; set => _local.Session = value; }
    private static ulong _sequence { get => _local.Sequence; set => _local.Sequence = value; }
    private static bool _active { get => _local.Active; set => _local.Active = value; }
    private static float _nextManifest { get => _local.NextManifest; set => _local.NextManifest = value; }
    private static float _closedUntil { get => _local.ClosedUntil; set => _local.ClosedUntil = value; }
    private static float _sessionStarted { get => _local.Started; set => _local.Started = value; }
    private static ushort _heartbeatModule { get => _local.Heartbeat; set => _local.Heartbeat = value; }
    private static readonly Dictionary<string, float> Failures = new(StringComparer.Ordinal);
    private static float _reportWindow;
    private static int _reportCount;

    private static uint _publicClaim, _observedPublicClaim;
    private static int LocalPeer => Math.Max(1, NetPlayerActors.LocalPlayerId());
    internal static int PublicAuthor
    {
        get
        {
            int author = PublicLane.Active ? LocalPeer : int.MaxValue;
            uint claim = PublicLane.Active ? _publicClaim : 0;
            foreach (var pair in Sessions)
            {
                var session = pair.Value;
                if (pair.Key >= 0 || !session.Active || Time.unscaledTime - session.LastSeenTime > 10f) continue;
                int peer = -pair.Key;
                if (session.PublicClaim > claim || session.PublicClaim == claim && peer < author)
                { author = peer; claim = session.PublicClaim; }
            }
            return author;
        }
    }
    internal static bool IsPublicAuthor => PublicAuthor == LocalPeer;
    internal static void ClaimPublicCatalog()
    {
        if (IsPublicAuthor) return;
        if (_observedPublicClaim == uint.MaxValue) return;
        _publicClaim = _observedPublicClaim + 1; _observedPublicClaim = _publicClaim;
        PublicLane.NextManifest = 0;
        foreach (LocalModule module in PublicLane.Modules.Values)
        { module.Last = null; module.Baseline = null; module.NextBaseline = module.NextRefresh = 0; }
    }
    internal static TownRackState? PublicRack
    {
        get
        {
            int peer = -PublicAuthor;
            // Authority handoff adopts what observers are displaying, including elapsed
            // analytic motion since the last packet. A stale owner sample must not rewind
            // a cassette which already completed while that owner disconnected.
            if (RemoteRacks.TryGetValue(peer, out var clocks))
                foreach (RackPlayback clock in clocks.Values)
                    if (clock.State != null)
                    {
                        if (!ReferenceEquals(clock.HandoffSource, clock.State))
                        { clock.HandoffSource = clock.State; clock.Handoff = clock.State.Copy(); }
                        TownRackState displayed = clock.Handoff!;
                        displayed.Page = clock.DisplayPage; displayed.From = clock.FromPage;
                        displayed.Elapsed = clock.Turning ? clock.Elapsed : TownRackState.TurnDuration;
                        return displayed;
                    }
            if (Pending.TryGetValue(peer, out var modules))
                foreach (TownServiceFrame frame in modules.Values) if (frame.Rack != null) return frame.Rack;
            return null;
        }
    }
    private sealed class LocalLane
    {
        internal readonly Dictionary<ushort, LocalModule> Modules = new();
        internal readonly Dictionary<ushort, TownRackState> Racks = new();
        internal readonly Dictionary<ushort, TownRackStamp> Members = new();
        internal readonly Dictionary<ushort, CanvasGroup> Gates = new();
        internal Transform? SharedFrame, Station;
        internal byte Service; internal uint Session; internal ulong Sequence;
        internal bool Active; internal float NextManifest, ClosedUntil, Started; internal ushort Heartbeat;
    }
    private static readonly LocalLane PrivateLane = new(), PublicLane = new();
    private static LocalLane _local = PrivateLane;
    internal static IDisposable UsePublicLane() => new LaneScope(PublicLane);
    private sealed class LaneScope : IDisposable
    {
        private readonly LocalLane _previous;
        internal LaneScope(LocalLane lane) { _previous = _local; _local = lane; }
        public void Dispose() => _local = _previous;
    }
    private sealed class LocalModule
    {
        internal ushort Id, Template;
        internal string Address = string.Empty;
        internal TownServiceBinding Binding = null!;
        internal TownServiceFrame? Last;
        internal TownServiceFrame? Baseline;
        internal float NextBaseline;
        internal float NextRefresh;
        internal float RetryAfter;
        internal bool HighPriority, WasPriority;
        internal ulong LastSent;
        internal readonly TownServiceFrame Probe = new();
        internal Func<Transform, bool>? Exclude;
    }
    private sealed class RemoteModule : IDisposable
    {
        internal GameObject Host = null!;
        internal Canvas? AddedCanvas;
        internal TownServiceBinding Binding = null!;
        internal uint Session;
        internal ushort Template;
        internal string Address = string.Empty;
        internal ulong Sequence;
        internal TownServiceFrame? LastFrame;
        internal Renderer[]? RackBodyRenderers;
        internal TownServiceMotion Motion = null!;
        public void Dispose()
        { Binding.Dispose(); if (Host != null) { Host.SetActive(false); Object.Destroy(Host); } }
    }

    internal static void RegisterTemplate(byte service, ushort template, Transform original,
        Func<Transform, bool>? exclude = null, string address = "")
    {
        string key = TemplateKey(service, template, address);
        if (Templates.TryGetValue(key, out GameObject? existing) && existing != null) return;
        if (original == null) throw new ArgumentException("Native town-service template is absent.");
        if (_templateHost == null)
        {
            _templateHost = new GameObject("GVR inert town-service template bank");
            _templateHost.SetActive(false); Object.DontDestroyOnLoad(_templateHost);
        }
        // Instantiate below an inactive parent BEFORE stripping components; no Awake/OnEnable.
        GameObject clone = Object.Instantiate(original.gameObject, _templateHost.transform, false);
        Prune(original, clone.transform, exclude);
        TownServiceNeutralize.Apply(clone);
        PrepareInertGeometry?.Invoke(address, clone);
        foreach (UnityEngine.UI.Graphic graphic in clone.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
        clone.SetActive(false);
        using var check = new TownServiceBinding(clone.transform);
        Templates[key] = clone;
    }

    internal static void ForgetTemplates(byte service, string addressPrefix)
    {
        // Called only after the corresponding resident has no local or remote visitors.
        // Its original Addressables/material owners may now be released; a later resident
        // must freeze fresh rendering resources, not reuse a dead decoration template.
        string prefix = service + ":" + addressPrefix;
        var keys = new List<string>();
        foreach (var pair in Templates) if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(pair.Key);
        foreach (string key in keys)
        { if (Templates[key] != null) Object.Destroy(Templates[key]); Templates.Remove(key); }
    }

    private static void Prune(Transform source, Transform copy, Func<Transform, bool>? exclude)
    {
        for (int i = source.childCount - 1; i >= 0; i--)
        {
            Transform child = source.GetChild(i), duplicate = copy.GetChild(i);
            if (TownServiceBinding.GeneratedTextMesh(child) || (exclude != null && exclude(child))) Object.DestroyImmediate(duplicate.gameObject);
            else Prune(child, duplicate, exclude);
        }
    }

    internal static void BeginSession(byte service, uint session, Transform sharedFrame, Transform stationAnchor, float ownerAge = 0f)
    {
        TemplateKey(service, 1);
        if (session == 0 || sharedFrame == null || stationAnchor == null) throw new ArgumentException("Missing town-service session frame.");
        if (_session != session || _service != service)
        { ClearLocalModules(); _nextManifest = 0; _sessionStarted = Time.unscaledTime - Mathf.Max(0f, ownerAge); }
        _service = service; _session = session; _sharedFrame = sharedFrame; _station = stationAnchor; _active = true;
    }

    internal static void RegisterModule(ushort module, ushort template, Transform liveRoot,
        Func<Transform, bool>? exclude = null, string address = "")
    {
        if (!_active || module >= TownServiceFrame.BundleStream || liveRoot == null
            || Local.Count >= TownServiceFrame.MaxModules && !Local.ContainsKey(module))
            throw new ArgumentException("Invalid live town-service module.");
        if (!Templates.ContainsKey(TemplateKey(_service, template, address)))
            throw new InvalidOperationException("Register the original template before its town-service module.");
        if (Local.TryGetValue(module, out LocalModule? current))
        {
            if (current.Template == template && current.Address == address && ReferenceEquals(current.Binding.Root, liveRoot)) return;
            current.Binding.Dispose();
        }
        Local[module] = new LocalModule { Id = module, Template = template, Address = address, Binding = new TownServiceBinding(liveRoot, exclude), Exclude = exclude };
        _nextManifest = 0;
    }

    internal static void UnregisterModule(ushort module)
    {
        if (!Local.TryGetValue(module, out LocalModule? current)) return;
        current.Binding.Dispose(); Local.Remove(module); LocalRacks.Remove(module); LocalRackMembers.Remove(module); LocalRackGates.Remove(module); _nextManifest = 0;
    }

    internal static void SetPriority(ushort module, bool highPriority)
    { if (Local.TryGetValue(module, out LocalModule? current)) current.HighPriority = highPriority; }

    private static void SnapshotSent(TownServiceFrame frame)
    {
        using var lane = new LaneScope(frame.PublicCatalog ? PublicLane : PrivateLane);
        if (frame.Session != _session || frame.Service != _service || !Local.TryGetValue(frame.Module, out LocalModule? module)) return;
        module.LastSent = Math.Max(module.LastSent, frame.Sequence);
        float now = Time.unscaledTime;
        if (frame.BaseSequence == 0 && module.Baseline?.Sequence == frame.Sequence)
            module.NextBaseline = now + 5f + module.Id % 13 * .07f;
        // A single small ordinary module maintains session liveness; unchanged stock
        // does not need 2,000 separate subsecond heartbeat packets behind it.
        module.NextRefresh = now + (module.Id == _heartbeatModule ? .75f : 5f + module.Id % 7 * .03f);
    }

    internal static void EndSession()
    { _active = false; _closedUntil = Time.unscaledTime + 5; _nextManifest = 0; ClearLocalModules(); }

    /// <summary>Call in the owner's final presentation pass. Immutable packets go to the existing transport.</summary>
    internal static void Capture(Action<byte[], int> send) => Capture((bytes, length, _) => send(bytes, length));
    internal static void Capture(Action<byte[], int, object?> send)
    {
        using (new LaneScope(PrivateLane)) CaptureLane(send);
        using (new LaneScope(PublicLane)) CaptureLane(send);
    }
    private static void CaptureLane(Action<byte[], int, object?> send)
    {
        float now = Time.unscaledTime;
        if (_session == 0 || _sharedFrame == null || (!_active && now > _closedUntil)) return;
        if (_active)
        {
            _heartbeatModule = ushort.MaxValue;
            foreach (ushort id in Local.Keys) if (id < _heartbeatModule) _heartbeatModule = id;
            SourceParents.Clear();
            foreach (LocalModule source in Local.Values)
                for (int i = 0; i < source.Binding.Nodes.Length; i++)
                    if (source.Binding.Nodes[i] != null) SourceParents[source.Binding.Nodes[i]] = new ParentLink(source.Id, source.Binding.Bindings[i]);
            foreach (LocalModule module in Local.Values)
            {
                if (now < module.RetryAfter) continue;
                try
                {
                    Transform source = module.Binding.Root;
                    if (source == null) continue;
                    TownServiceNode[] nodes;
                    try { nodes = module.Binding.Read(Assets); }
                    catch (InvalidDataException e) when (e.Message == "Native town-service topology changed.")
                    {
                        module.Binding.Dispose(); module.Binding = new TownServiceBinding(source, module.Exclude);
                        nodes = module.Binding.Read(Assets);
                    }
                    // Reuse only the unpublished probe. Emitted headers/node arrays are
                    // retained separately, so a later read cannot mutate queued artwork.
                    TownServiceFrame frame = module.Probe;
                    frame.PublicCatalog = ReferenceEquals(_local, PublicLane); frame.PublicClaim = frame.PublicCatalog ? _publicClaim : 0;
                    frame.Service = _service; frame.Session = _session; frame.Module = module.Id;
                    frame.Template = module.Template; frame.TemplateAddress = module.Address; frame.Structure = module.Binding.Structure;
                    frame.Visible = source.gameObject.activeInHierarchy; frame.SampleTime = now;
                    frame.Pose = ReadPose(source, _sharedFrame, frame.Pose); frame.Nodes = nodes;
                    frame.RackMember = LocalRackMembers.TryGetValue(module.Id, out TownRackStamp? rackMember) ? rackMember : null;
                    frame.Rack = LocalRacks.TryGetValue(module.Id, out TownRackState? rackState) ? rackState : null;
                    frame.ParentModule = TownServiceFrame.ManifestModule; frame.ParentBinding = 0;
                    ResetCanvasFrame(frame);
                    ReadParent(module, frame); ReadCanvasFrame(source, frame);
                    if (frame.RackMember != null)
                    {
                        float alpha = ReadRackAlpha(module);
                        if (frame.RackMember.Alpha != alpha) { frame.RackMember = frame.RackMember.Copy(); frame.RackMember.Alpha = alpha; }
                    }
                    if (SamePresentation(module.Last, frame)
                        && (now < module.NextRefresh || module.Last != null && module.LastSent < module.Last.Sequence)) continue;
                    frame.Sequence = NextSequence();
                    TownServiceFrame emitted;
                    if (module.Baseline == null || now >= module.NextBaseline || !TownServiceDelta.Compatible(module.Baseline, frame))
                    { emitted = TownServiceDelta.Retain(frame); module.Baseline = emitted; module.NextBaseline = float.PositiveInfinity; }
                    else emitted = TownServiceDelta.Create(module.Baseline, frame);
                    emitted.HighPriority = module.HighPriority || module.WasPriority || module.Id == _heartbeatModule;
                    module.WasPriority = module.HighPriority;
                    byte[] packet = TownServiceCodec.Write(emitted);
                    send(packet, packet.Length, emitted); module.Last = TownServiceDelta.Retain(frame); module.NextRefresh = now + .75f + module.Id % 7 * .03f;
                }
                catch (Exception e) { module.RetryAfter = now + 1; Report("capture module " + module.Id, e); }
            }
        }
        if (now >= _nextManifest)
        {
            try
            {
                var ids = new List<ushort>(Local.Keys); ids.Sort();
                var manifest = new TownServiceFrame { PublicCatalog = ReferenceEquals(_local, PublicLane), PublicClaim = ReferenceEquals(_local, PublicLane) ? _publicClaim : 0, Service = _service, Session = _session,
                    Module = TownServiceFrame.ManifestModule, Sequence = NextSequence(), SampleTime = now,
                    SessionAge = now - _sessionStarted,
                    Visible = _active, Modules = ids.ToArray(), Pose = _station != null ? ReadPose(_station, _sharedFrame) : IdentityPose() };
                byte[] packet = TownServiceCodec.Write(manifest); send(packet, packet.Length, manifest); _nextManifest = now + (_active ? 5f : .5f);
            }
            catch (Exception e) { Report("capture manifest", e); }
        }
    }

    /// <summary>Receives a complete GVR1 module packet after ExtrasFragments assembly.</summary>
    internal static bool Receive(int peer, byte[] packet, int length)
    {
        if (peer <= 0 || !TownServiceCodec.TryRead(packet, length, out TownServiceFrame? frame)) return false;
        if (frame!.PublicCatalog) { peer = -peer; _observedPublicClaim = Math.Max(_observedPublicClaim, frame.PublicClaim); }
        if (!Sessions.ContainsKey(peer) && Sessions.Count >= 16) return true;
        if (frame!.Module == TownServiceFrame.ManifestModule)
        {
            if (Sessions.TryGetValue(peer, out TownServiceSessionInfo? previous) && frame.Sequence <= previous.Sequence) return true;
            if (previous != null && (previous.Session != frame.Session || previous.Service != frame.Service)) ClearRemoteModules(peer);
            Sessions[peer] = new TownServiceSessionInfo { Peer = peer, PublicClaim = frame.PublicClaim, Service = frame.Service, Session = frame.Session,
                Sequence = frame.Sequence, SampleTime = frame.SampleTime, ReceivedTime = Time.unscaledTime, LastSeenTime = Time.unscaledTime, Active = frame.Visible,
                SessionAge = frame.SessionAge,
                Modules = frame.Modules, Position = Position(frame.Pose), Rotation = Rotation(frame.Pose), Scale = Scale(frame.Pose) };
            if (peer > 0) VisitorSessions[peer] = Sessions[peer];
            if (!frame.Visible) ClearRemoteModules(peer);
            else if (Remote.TryGetValue(peer, out Dictionary<ushort, RemoteModule>? standing))
            {
                var removed = new List<ushort>();
                foreach (var pair in standing) if (Array.BinarySearch(frame.Modules, pair.Key) < 0 && !RackRetains(peer, pair.Key)) removed.Add(pair.Key);
                foreach (ushort id in removed) { standing[id].Dispose(); standing.Remove(id); }
            }
            PrunePending(Pending, peer, frame);
            PrunePending(ReceivedBaselines, peer, frame);
            ReconcileMerchantOffering(peer);
            return true;
        }
        if (!Pending.TryGetValue(peer, out Dictionary<ushort, TownServiceFrame>? pending))
        {
            if (Pending.Count >= 16) return true;
            pending = new Dictionary<ushort, TownServiceFrame>(); Pending.Add(peer, pending);
        }
        if (Sessions.TryGetValue(peer, out TownServiceSessionInfo? live) && live.Active
            && live.Session == frame.Session && live.Service == frame.Service && Array.BinarySearch(live.Modules, frame.Module) >= 0)
            live.LastSeenTime = Time.unscaledTime;
        if (pending.Count >= TownServiceFrame.MaxModules && !pending.ContainsKey(frame.Module)) return true;
        // A keyframe may finish after a newer delta. Keep it even though its sample is older:
        // the waiting cumulative delta names this exact baseline and then becomes usable.
        if (frame.BaseSequence == 0)
        {
            if (!ReceivedBaselines.TryGetValue(peer, out Dictionary<ushort, TownServiceFrame>? baselines))
            { baselines = new Dictionary<ushort, TownServiceFrame>(); ReceivedBaselines.Add(peer, baselines); }
            if (!baselines.TryGetValue(frame.Module, out TownServiceFrame? before) || frame.Sequence > before.Sequence)
                baselines[frame.Module] = frame;
        }
        if (!pending.TryGetValue(frame.Module, out TownServiceFrame? old) || frame.Sequence > old.Sequence)
            { pending[frame.Module] = frame; ObserveMerchantOffering(peer, frame); }
        return true;
    }

    private static void PrunePending(Dictionary<int, Dictionary<ushort, TownServiceFrame>> store, int peer, TownServiceFrame manifest)
    {
        if (!store.TryGetValue(peer, out Dictionary<ushort, TownServiceFrame>? modules)) return;
        var removed = new List<ushort>();
        foreach (var pair in modules)
            // Independent module lanes can overtake an older census/close packet. Keep
            // newer complete baselines until their own census arrives; TickRemote still
            // gates rendering by the current session and module membership.
            if (!RackRetains(peer, pair.Key) && pair.Value.Sequence <= manifest.Sequence && (!manifest.Visible
                || pair.Value.Service != manifest.Service || pair.Value.Session != manifest.Session
                || Array.BinarySearch(manifest.Modules, pair.Key) < 0)) removed.Add(pair.Key);
        foreach (ushort module in removed) modules.Remove(module);
    }

    /// <summary>Shared frame is supplied by the room owner. No observer-local fit or gaze pose is consulted.</summary>
    internal static void TickRemote(Func<int, Transform?> sharedFrame)
    {
        float now = Time.unscaledTime;
        foreach (var entry in Sessions)
        {
            TownServiceSessionInfo session = entry.Value;
            if (!session.Active || now - session.LastSeenTime > 10)
            { ClearRemoteModules(entry.Key); session.Active = false; continue; }
            if (entry.Key < 0 && -entry.Key != PublicAuthor)
            { ClearRemoteModules(entry.Key); continue; }
            Transform? parent = sharedFrame(entry.Key);
            if (parent == null || !Pending.TryGetValue(entry.Key, out Dictionary<ushort, TownServiceFrame>? pending)) continue;
            if (!Remote.TryGetValue(entry.Key, out Dictionary<ushort, RemoteModule>? standing))
            { standing = new Dictionary<ushort, RemoteModule>(); Remote.Add(entry.Key, standing); }
            foreach (RemoteModule visible in standing.Values) { visible.Motion.Tick(now); visible.Binding.TickAnimation(now); }
            UpdateRackClocks(entry.Key, pending, now);
            bool reorder = false;
            foreach (var packet in pending)
            {
                TownServiceFrame received = packet.Value;
                long retryKey = ((long)entry.Key << 16) | received.Module;
                if (RemoteRetry.TryGetValue(retryKey, out float retryAt) && now < retryAt) continue;
                standing.TryGetValue(received.Module, out RemoteModule? module);
                if (module != null && received.Sequence <= module.Sequence) continue;
                TownServiceFrame? baseline = null;
                if (ReceivedBaselines.TryGetValue(entry.Key, out Dictionary<ushort, TownServiceFrame>? baselines)) baselines.TryGetValue(received.Module, out baseline);
                TownServiceFrame? expanded = TownServiceDelta.Expand(baseline, received);
                if (expanded == null) continue;
                TownServiceFrame frame = expanded;
                if (frame.Session != session.Session || frame.Service != session.Service || Array.BinarySearch(session.Modules, frame.Module) < 0) continue;
                try
                {
                    if (!frame.Visible)
                    { if (module != null) { module.Host.SetActive(false); module.Motion.Reset(); module.Sequence = frame.Sequence; module.LastFrame = frame; } continue; }
                    Transform mount = parent;
                    if (frame.ParentModule != TownServiceFrame.ManifestModule)
                    {
                        if (!standing.TryGetValue(frame.ParentModule, out RemoteModule? parentModule)) continue;
                        int index = Array.IndexOf(parentModule.Binding.Bindings, frame.ParentBinding);
                        if (index < 0) throw new InvalidDataException("Original town-service parent binding is absent.");
                        mount = parentModule.Binding.Nodes[index];
                        if (module != null && mount.IsChildOf(module.Host.transform))
                            throw new InvalidDataException("Cyclic town-service module parent.");
                    }
                    if (module == null || module.Template != frame.Template || module.Address != frame.TemplateAddress || module.Session != frame.Session
                        || (module.AddedCanvas != null) != NeedsCanvas(frame))
                    {
                        RemoteModule candidate = BuildRemote(frame, mount);
                        try { candidate.Binding.Validate(frame, Assets); candidate.Binding.Apply(frame, Assets); }
                        catch { candidate.Dispose(); throw; }
                        module?.Dispose(); module = candidate; standing[frame.Module] = module;
                    }
                    else
                    {
                        // Resolve all assets before a tween or binding can change visible content.
                        module.Binding.Validate(frame, Assets);
                        if (!module.Host.activeSelf || module.LastFrame?.ParentModule != frame.ParentModule || module.LastFrame?.ParentBinding != frame.ParentBinding)
                            module.Motion.Reset();
                        module.Motion.BeforeApply(now); module.Binding.Apply(frame, Assets);
                    }
                    Transform root = module.Binding.Root;
                    module.Host.transform.SetParent(mount, false);
                    if (frame.ParentModule != TownServiceFrame.ManifestModule
                        && frame.Nodes[0].Values.TryGetValue(TownServiceProperty.Sibling, out TownServiceValue? sibling))
                        module.Host.transform.SetSiblingIndex((int)sibling.Numbers[0]);
                    module.Host.GetComponent<CanvasGroup>().alpha = frame.ParentAlpha;
                    if (frame.HasCanvasFrame && module.AddedCanvas != null)
                    {
                        ApplyCanvasFrame(module, frame, parent);
                    }
                    Transform poseRoot = module.AddedCanvas != null && !frame.HasCanvasFrame ? module.Host.transform : root;
                    poseRoot.position = mount.TransformPoint(Position(frame.Pose)); poseRoot.rotation = mount.rotation * Rotation(frame.Pose);
                    Vector3 worldScale = Vector3.Scale(mount.lossyScale, Scale(frame.Pose)), parentScale = poseRoot.parent.lossyScale;
                    poseRoot.localScale = new Vector3(worldScale.x / parentScale.x, worldScale.y / parentScale.y, worldScale.z / parentScale.z);
                    if (module.AddedCanvas != null && !frame.HasCanvasFrame)
                    {
                        if (root is RectTransform rr && module.Host.transform is RectTransform hostRect)
                        { hostRect.pivot = rr.pivot; hostRect.sizeDelta = rr.rect.size; }
                        root.localPosition = Vector3.zero; root.localRotation = Quaternion.identity; root.localScale = Vector3.one;
                    }
                    if (module.LastFrame != null && module.Host.activeSelf && module.LastFrame.ParentModule == frame.ParentModule
                        && module.LastFrame.ParentBinding == frame.ParentBinding)
                        module.Motion.AfterApply(now, frame.SampleTime - module.LastFrame.SampleTime);
                    RemoteRetry.Remove(retryKey); module.Sequence = frame.Sequence; module.LastFrame = frame; reorder = true;
                    module.Host.SetActive(true); root.gameObject.SetActive(true);
                }
                catch (Exception e)
                {
                    if (module != null) { module.Host.SetActive(false); module.Motion.Reset(); }
                    if (RemoteRetry.Count >= 8 * TownServiceFrame.MaxModules && !RemoteRetry.ContainsKey(retryKey)) RemoteRetry.Clear();
                    RemoteRetry[retryKey] = now + .25f;
                    Report("remote module " + entry.Key + "/" + frame.Module, e);
                }
            }
            if (reorder) OrderOriginalSiblings(standing);
            TickRackClocks(entry.Key, standing, now);
        }
    }

    private readonly struct SiblingRank
    { internal readonly Transform Node; internal readonly int Index;
        internal SiblingRank(Transform node, int index) { Node = node; Index = index; } }
    private static readonly Dictionary<Transform, List<SiblingRank>> OrderGroups = new();
    private static readonly List<List<SiblingRank>> OrderLists = new();
    private static void OrderOriginalSiblings(Dictionary<ushort, RemoteModule> modules)
    {
        foreach (RemoteModule module in modules.Values)
        {
            TownServiceFrame? frame = module.LastFrame; if (frame == null) continue;
            if (frame.ParentModule != TownServiceFrame.ManifestModule && frame.Nodes[0].Values.TryGetValue(TownServiceProperty.Sibling, out TownServiceValue? rootOrder))
                AddOrder(module.Host.transform, (int)rootOrder.Numbers[0]);
            for (int i = 1; i < frame.Nodes.Length; i++)
                if (frame.Nodes[i].Values.TryGetValue(TownServiceProperty.Sibling, out TownServiceValue? order))
                    AddOrder(module.Binding.Nodes[i], (int)order.Numbers[0]);
        }
        foreach (List<SiblingRank> group in OrderGroups.Values)
        {
            group.Sort((a, b) => a.Index.CompareTo(b.Index));
            for (int i = 0; i < group.Count; i++) group[i].Node.SetSiblingIndex(i);
            group.Clear();
        }
        OrderGroups.Clear();
    }
    private static void AddOrder(Transform node, int index)
    {
        if (node.parent == null) return;
        if (!OrderGroups.TryGetValue(node.parent, out List<SiblingRank>? list))
        {
            int next = OrderGroups.Count;
            if (next == OrderLists.Count) OrderLists.Add(new List<SiblingRank>());
            list = OrderLists[next]; OrderGroups.Add(node.parent, list);
        }
        list.Add(new SiblingRank(node, index));
    }
    private static RemoteModule BuildRemote(TownServiceFrame frame, Transform sharedFrame)
    {
        string key = TemplateKey(frame.Service, frame.Template, frame.TemplateAddress);
        if (!Templates.ContainsKey(key)) ResolveTemplate?.Invoke(frame.Service, frame.Template, frame.TemplateAddress);
        if (!Templates.TryGetValue(key, out GameObject? template) || template == null)
            throw new InvalidDataException("Original town-service template is not registered on this client.");
        var host = new GameObject("GloomhavenVR.TownService.Observer", typeof(RectTransform));
        host.SetActive(false); host.transform.SetParent(sharedFrame, false);
        Canvas? canvas = null;
        if (NeedsCanvas(frame))
        {
            canvas = host.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Rig.VRRigDriver.HeadCamera != null ? Rig.VRRigDriver.HeadCamera : Camera.main;
        }
        CanvasGroup group = host.AddComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
        GameObject clone = Object.Instantiate(template, host.transform, false);
        // Templates are already inert; repeat the invariant before the clone can become active.
        TownServiceNeutralize.Apply(clone);
        PrepareInertGeometry?.Invoke(frame.TemplateAddress, clone);
        VRLayers.Apply(host);
        foreach (Canvas originalCanvas in clone.GetComponentsInChildren<Canvas>(true))
            originalCanvas.worldCamera = Rig.VRRigDriver.HeadCamera != null ? Rig.VRRigDriver.HeadCamera : Camera.main;
        var binding = new TownServiceBinding(clone.transform);
        return new RemoteModule { Host = host, AddedCanvas = canvas, Binding = binding,
            Motion = new TownServiceMotion(host.transform, binding.Nodes), Session = frame.Session,
            Template = frame.Template, Address = frame.TemplateAddress };
    }

    private static bool NeedsCanvas(TownServiceFrame frame) => frame.HasCanvasFrame
        || frame.ParentModule == TownServiceFrame.ManifestModule && !frame.Nodes[0].Values.ContainsKey(TownServiceProperty.Canvas);
    private static void ApplyCanvasFrame(RemoteModule module, TownServiceFrame frame, Transform shared)
    {
        Transform host = module.Host.transform;
        host.position = shared.TransformPoint(Position(frame.CanvasPose)); host.rotation = shared.rotation * Rotation(frame.CanvasPose);
        Vector3 desired = Vector3.Scale(shared.lossyScale, Scale(frame.CanvasPose)), basis = host.parent.lossyScale;
        host.localScale = new Vector3(desired.x / basis.x, desired.y / basis.y, desired.z / basis.z);
        RectTransform rect = (RectTransform)host; rect.sizeDelta = new Vector2(frame.CanvasRect[0], frame.CanvasRect[1]);
        rect.pivot = new Vector2(frame.CanvasRect[2], frame.CanvasRect[3]);
        Canvas canvas = module.AddedCanvas!;
        canvas.referencePixelsPerUnit = frame.CanvasSettings[0]; canvas.pixelPerfect = frame.CanvasSettings[1] != 0;
        canvas.overridePixelPerfect = frame.CanvasSettings[2] != 0; canvas.scaleFactor = frame.CanvasSettings[3];
        canvas.additionalShaderChannels = (AdditionalCanvasShaderChannels)(int)frame.CanvasSettings[4];
        canvas.sortingOrder = frame.CanvasSortingOrder; canvas.sortingLayerID = frame.CanvasSortingLayer;
    }
    internal static void RemovePeer(int peer)
    { MerchantOfferings.Remove(peer); ClearRemoteModules(peer); Pending.Remove(peer); ReceivedBaselines.Remove(peer); Sessions.Remove(peer); VisitorSessions.Remove(peer);
      ClearRemoteModules(-peer); Pending.Remove(-peer); ReceivedBaselines.Remove(-peer); Sessions.Remove(-peer); }
    internal static void RequestFullRefresh()
    {
        foreach (LocalModule module in AllLocalModules())
        { module.Last = null; module.Baseline = null; module.NextRefresh = module.NextBaseline = 0; }
        PrivateLane.NextManifest = PublicLane.NextManifest = 0;
    }
    private static IEnumerable<LocalModule> AllLocalModules()
    { foreach (LocalModule module in PrivateLane.Modules.Values) yield return module;
      foreach (LocalModule module in PublicLane.Modules.Values) yield return module; }
    internal static void ResetNetwork()
    {
        foreach (int peer in new List<int>(Remote.Keys)) ClearRemoteModules(peer);
        MerchantOfferings.Clear(); Pending.Clear(); ReceivedBaselines.Clear(); Sessions.Clear(); VisitorSessions.Clear(); RemoteRetry.Clear(); foreach (LocalModule module in AllLocalModules())
        { module.Last = null; module.Baseline = null; module.NextRefresh = module.NextBaseline = 0; }
        PrivateLane.NextManifest = PublicLane.NextManifest = 0;
    }
    internal static void Shutdown()
    {
        ResetNetwork(); using (new LaneScope(PublicLane)) { ClearLocalModules(); _session = 0; _active = false; }
        ClearLocalModules(); Templates.Clear();
        if (_templateHost != null) Object.Destroy(_templateHost);
        _templateHost = null; _session = 0; _service = 0; _active = false; _station = _sharedFrame = null;
        SourceParents.Clear(); ParentGroups.Clear(); TownServiceMaterial.Reset(); Assets.Clear();
        ReportReset();
    }
    private static void ClearLocalModules()
    { LocalRacks.Clear(); LocalRackMembers.Clear(); LocalRackGates.Clear(); foreach (LocalModule module in Local.Values) module.Binding.Dispose(); Local.Clear(); }
    private static void ClearRemoteModules(int peer)
    { RemoteRacks.Remove(peer); if (!Remote.TryGetValue(peer, out Dictionary<ushort, RemoteModule>? modules)) return;
        foreach (RemoteModule module in modules.Values) module.Dispose(); Remote.Remove(peer); }
    private static string TemplateKey(byte service, ushort template, string address = "")
    { if (service < 1 || service > 3 || template == 0) throw new ArgumentException("Invalid town-service template identity.");
        return service + ":" + (address.Length == 0 ? template.ToString() : address); }
    private static ulong NextSequence() { if (++_sequence == 0) ++_sequence; return _sequence; }
    private static float[] ReadPose(Transform source, Transform frame, float[]? destination = null)
    {
        Vector3 p = frame.InverseTransformPoint(source.position), s = source.lossyScale, basis = frame.lossyScale;
        Quaternion q = Quaternion.Inverse(frame.rotation) * source.rotation;
        float[] result = destination ?? new float[10];
        result[0]=p.x; result[1]=p.y; result[2]=p.z; result[3]=q.x; result[4]=q.y; result[5]=q.z; result[6]=q.w;
        result[7]=s.x/basis.x; result[8]=s.y/basis.y; result[9]=s.z/basis.z;
        return result;
    }
    private static float[] IdentityPose() => new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f };
    private static Vector3 Position(float[] pose) => new(pose[0], pose[1], pose[2]);
    private static Quaternion Rotation(float[] pose) => new(pose[3], pose[4], pose[5], pose[6]);
    private static Vector3 Scale(float[] pose) => new(pose[7], pose[8], pose[9]);
    private static bool SamePresentation(TownServiceFrame? a, TownServiceFrame b)
    {
        if ((a?.RackMember == null) != (b.RackMember == null) || a?.RackMember != null && !a.RackMember.Same(b.RackMember)) return false;
        if (a != null && (a.PublicCatalog != b.PublicCatalog || a.PublicClaim != b.PublicClaim)) return false;
        if ((a?.Rack == null) != (b.Rack == null) || a?.Rack != null && !a.Rack.Same(b.Rack)) return false;
        if (a == null || a.Visible != b.Visible || a.Structure != b.Structure || a.Nodes.Length != b.Nodes.Length
            || a.ParentModule != b.ParentModule || a.ParentBinding != b.ParentBinding || a.ParentAlpha != b.ParentAlpha
            || a.HasCanvasFrame != b.HasCanvasFrame || a.CanvasSortingLayer != b.CanvasSortingLayer || a.CanvasSortingOrder != b.CanvasSortingOrder) return false;
        for (int i = 0; i < 10; i++) if (a.Pose[i] != b.Pose[i] || a.CanvasPose[i] != b.CanvasPose[i]) return false;
        for (int i = 0; i < 4; i++) if (a.CanvasRect[i] != b.CanvasRect[i]) return false;
        for (int i = 0; i < 5; i++) if (a.CanvasSettings[i] != b.CanvasSettings[i]) return false;
        for (int i = 0; i < a.Nodes.Length; i++)
        {
            if (ReferenceEquals(a.Nodes[i], b.Nodes[i])) continue;
            if (a.Nodes[i].Binding != b.Nodes[i].Binding || a.Nodes[i].Values.Count != b.Nodes[i].Values.Count) return false;
            foreach (var pair in a.Nodes[i].Values)
                if (!b.Nodes[i].Values.TryGetValue(pair.Key, out TownServiceValue? value) || !pair.Value.Same(value)) return false;
        }
        return true;
    }
    private static void ReadCanvasFrame(Transform source, TownServiceFrame frame)
    {
        if (frame.ParentModule != TownServiceFrame.ManifestModule || _sharedFrame == null) return;
        source.GetComponentsInParent(true, ParentCanvases);
        Canvas? canvas = null;
        foreach (Canvas candidate in ParentCanvases) if (candidate.isActiveAndEnabled) { canvas = candidate.rootCanvas; break; }
        if (canvas == null || canvas.transform == source || canvas.transform is not RectTransform rect) return;
        frame.HasCanvasFrame = true; frame.CanvasPose = ReadPose(canvas.transform, _sharedFrame, frame.CanvasPose);
        frame.CanvasRect[0]=rect.rect.width; frame.CanvasRect[1]=rect.rect.height; frame.CanvasRect[2]=rect.pivot.x; frame.CanvasRect[3]=rect.pivot.y;
        frame.CanvasSettings[0]=canvas.referencePixelsPerUnit; frame.CanvasSettings[1]=canvas.pixelPerfect?1f:0f;
        frame.CanvasSettings[2]=canvas.overridePixelPerfect?1f:0f; frame.CanvasSettings[3]=canvas.scaleFactor; frame.CanvasSettings[4]=(float)canvas.additionalShaderChannels;
        frame.CanvasSortingLayer = canvas.sortingLayerID; frame.CanvasSortingOrder = canvas.sortingOrder;
    }
    private static void ResetCanvasFrame(TownServiceFrame frame)
    {
        frame.HasCanvasFrame=false; frame.CanvasSortingLayer=frame.CanvasSortingOrder=0;
        Array.Clear(frame.CanvasPose,0,frame.CanvasPose.Length);
        frame.CanvasPose[6]=frame.CanvasPose[7]=frame.CanvasPose[8]=frame.CanvasPose[9]=1f;
        frame.CanvasRect[0]=frame.CanvasRect[1]=100f; frame.CanvasRect[2]=frame.CanvasRect[3]=.5f;
        frame.CanvasSettings[0]=100f;frame.CanvasSettings[1]=frame.CanvasSettings[2]=frame.CanvasSettings[4]=0f;frame.CanvasSettings[3]=1f;
    }
    private static void ReadParent(LocalModule module, TownServiceFrame frame)
    {
        float alpha = 1;
        for (Transform? parent = module.Binding.Root.parent; parent != null; parent = parent.parent)
        {
            if (SourceParents.TryGetValue(parent, out ParentLink link) && link.Module != module.Id)
            {
                frame.ParentModule = link.Module; frame.ParentBinding = link.Binding;
                frame.ParentAlpha = alpha; frame.Pose = ReadPose(module.Binding.Root, parent, frame.Pose); return;
            }
            bool stop = false;
            parent.GetComponents(ParentGroups);
            foreach (CanvasGroup group in ParentGroups)
                if (group.enabled) { alpha *= group.alpha; if (group.ignoreParentGroups) stop = true; }
            if (stop) break;
        }
        frame.ParentAlpha = alpha;
    }
    private static void ReportReset()
    {
        Failures.Clear(); _reportWindow = 0; _reportCount = 0;
    }
    private static void Report(string phase, Exception error)
    {
        string key = phase + ": " + error.Message; float now = Time.unscaledTime;
        if (now - _reportWindow >= 30f) { _reportWindow = now; _reportCount = 0; }
        if (_reportCount >= 8)
        {
            if (_reportCount == 8) { _reportCount++; VRLog.Note("TownServices", "Further presentation errors are suppressed for this 30-second interval."); }
            return;
        }
        if (Failures.TryGetValue(key, out float last) && now - last < 30) return;
        if (Failures.Count > 32) Failures.Clear();
        Failures[key] = now; _reportCount++;
        VRLog.Note("TownServices", "Original service presentation unavailable (" + key + ").");
        PresentationUnavailable?.Invoke(key);
    }
}
