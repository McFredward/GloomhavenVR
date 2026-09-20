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
    internal byte Service;
    internal uint Session;
    internal ulong Sequence;
    internal float SampleTime, ReceivedTime, SessionAge;
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
internal static class TownServiceMirror
{
    internal static readonly TownServiceAssets Assets = new();
    private static readonly Dictionary<int, TownServiceSessionInfo> Sessions = new();
    internal static IReadOnlyDictionary<int, TownServiceSessionInfo> RemoteSessions => Sessions;
    internal static event Action<string>? PresentationUnavailable;
    internal static Func<int, Transform?>? SharedFrameForRemote { get; set; }
    private static readonly Dictionary<string, GameObject> Templates = new(StringComparer.Ordinal);
    internal static Func<byte, ushort, string, bool>? ResolveTemplate { get; set; }
    private static readonly Dictionary<ushort, LocalModule> Local = new();
    private static readonly Dictionary<int, Dictionary<ushort, RemoteModule>> Remote = new();
    private static readonly Dictionary<int, Dictionary<ushort, TownServiceFrame>> Pending = new();
    private static readonly Dictionary<int, Dictionary<ushort, TownServiceFrame>> ReceivedBaselines = new();
    private readonly struct ParentLink
    { internal readonly ushort Module; internal readonly uint Binding;
        internal ParentLink(ushort module, uint binding) { Module = module; Binding = binding; } }
    private static readonly Dictionary<Transform, ParentLink> SourceParents = new();
    private static readonly List<CanvasGroup> ParentGroups = new(2);
    private static GameObject? _templateHost;
    private static Transform? _sharedFrame, _station;
    private static byte _service;
    private static uint _session;
    private static ulong _sequence;
    private static bool _active;
    private static float _nextManifest, _closedUntil;
    private static float _sessionStarted;
    private static readonly Dictionary<string, float> Failures = new(StringComparer.Ordinal);

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
        foreach (UnityEngine.UI.Graphic graphic in clone.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
        clone.SetActive(false);
        using var check = new TownServiceBinding(clone.transform);
        Templates[key] = clone;
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
        if (!_active || module == TownServiceFrame.ManifestModule || liveRoot == null
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
        current.Binding.Dispose(); Local.Remove(module); _nextManifest = 0;
    }

    internal static void EndSession()
    { _active = false; _closedUntil = Time.unscaledTime + 5; _nextManifest = 0; ClearLocalModules(); }

    /// <summary>Call in the owner's final presentation pass. Immutable packets go to the existing transport.</summary>
    internal static void Capture(Action<byte[], int> send) => Capture((bytes, length, _) => send(bytes, length));
    internal static void Capture(Action<byte[], int, object?> send)
    {
        float now = Time.unscaledTime;
        if (_session == 0 || _sharedFrame == null || (!_active && now > _closedUntil)) return;
        if (_active)
        {
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
                    var frame = new TownServiceFrame { Service = _service, Session = _session, Module = module.Id,
                        Template = module.Template, TemplateAddress = module.Address, Structure = module.Binding.Structure, Visible = source.gameObject.activeInHierarchy,
                        SampleTime = now, Pose = ReadPose(source, _sharedFrame), Nodes = nodes };
                    ReadParent(module, frame);
                    if (SamePresentation(module.Last, frame) && now < module.NextRefresh) continue;
                    frame.Sequence = NextSequence();
                    TownServiceFrame emitted;
                    if (module.Baseline == null || now >= module.NextBaseline || !TownServiceDelta.Compatible(module.Baseline, frame))
                    { emitted = frame; module.Baseline = TownServiceDelta.Retain(frame); module.NextBaseline = now + 5f + module.Id % 13 * .07f; }
                    else emitted = TownServiceDelta.Create(module.Baseline, frame);
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
                var manifest = new TownServiceFrame { Service = _service, Session = _session,
                    Module = TownServiceFrame.ManifestModule, Sequence = NextSequence(), SampleTime = now,
                    SessionAge = now - _sessionStarted,
                    Visible = _active, Modules = ids.ToArray(), Pose = _station != null ? ReadPose(_station, _sharedFrame) : IdentityPose() };
                byte[] packet = TownServiceCodec.Write(manifest); send(packet, packet.Length, manifest); _nextManifest = now + .5f;
            }
            catch (Exception e) { Report("capture manifest", e); }
        }
    }

    /// <summary>Receives a complete GVR1 module packet after ExtrasFragments assembly.</summary>
    internal static bool Receive(int peer, byte[] packet, int length)
    {
        if (peer <= 0 || !TownServiceCodec.TryRead(packet, length, out TownServiceFrame? frame)) return false;
        if (!Sessions.ContainsKey(peer) && Sessions.Count >= 8) return true;
        if (frame!.Module == TownServiceFrame.ManifestModule)
        {
            if (Sessions.TryGetValue(peer, out TownServiceSessionInfo? previous) && frame.Sequence <= previous.Sequence) return true;
            if (previous != null && (previous.Session != frame.Session || previous.Service != frame.Service)) ClearRemoteModules(peer);
            Sessions[peer] = new TownServiceSessionInfo { Peer = peer, Service = frame.Service, Session = frame.Session,
                Sequence = frame.Sequence, SampleTime = frame.SampleTime, ReceivedTime = Time.unscaledTime, Active = frame.Visible,
                SessionAge = frame.SessionAge,
                Modules = frame.Modules, Position = Position(frame.Pose), Rotation = Rotation(frame.Pose), Scale = Scale(frame.Pose) };
            if (!frame.Visible) ClearRemoteModules(peer);
            else if (Remote.TryGetValue(peer, out Dictionary<ushort, RemoteModule>? standing))
            {
                var removed = new List<ushort>();
                foreach (var pair in standing) if (Array.BinarySearch(frame.Modules, pair.Key) < 0) removed.Add(pair.Key);
                foreach (ushort id in removed) { standing[id].Dispose(); standing.Remove(id); }
            }
            PrunePending(Pending, peer, frame);
            PrunePending(ReceivedBaselines, peer, frame);
            return true;
        }
        if (!Pending.TryGetValue(peer, out Dictionary<ushort, TownServiceFrame>? pending))
        {
            if (Pending.Count >= 8) return true;
            pending = new Dictionary<ushort, TownServiceFrame>(); Pending.Add(peer, pending);
        }
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
            pending[frame.Module] = frame;
        return true;
    }

    private static void PrunePending(Dictionary<int, Dictionary<ushort, TownServiceFrame>> store, int peer, TownServiceFrame manifest)
    {
        if (!store.TryGetValue(peer, out Dictionary<ushort, TownServiceFrame>? modules)) return;
        var removed = new List<ushort>();
        foreach (var pair in modules)
            if (!manifest.Visible || pair.Value.Service != manifest.Service || pair.Value.Session != manifest.Session
                || Array.BinarySearch(manifest.Modules, pair.Key) < 0) removed.Add(pair.Key);
        foreach (ushort module in removed) modules.Remove(module);
    }

    /// <summary>Shared frame is supplied by the room owner. No observer-local fit or gaze pose is consulted.</summary>
    internal static void TickRemote(Func<int, Transform?> sharedFrame)
    {
        float now = Time.unscaledTime;
        foreach (var entry in Sessions)
        {
            TownServiceSessionInfo session = entry.Value;
            if (!session.Active || now - session.ReceivedTime > 10)
            { ClearRemoteModules(entry.Key); session.Active = false; continue; }
            Transform? parent = sharedFrame(entry.Key);
            if (parent == null || !Pending.TryGetValue(entry.Key, out Dictionary<ushort, TownServiceFrame>? pending)) continue;
            if (!Remote.TryGetValue(entry.Key, out Dictionary<ushort, RemoteModule>? standing))
            { standing = new Dictionary<ushort, RemoteModule>(); Remote.Add(entry.Key, standing); }
            foreach (var packet in pending)
            {
                TownServiceFrame received = packet.Value;
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
                    { if (module != null) { module.Host.SetActive(false); module.Sequence = frame.Sequence; } continue; }
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
                    if (module == null || module.Template != frame.Template || module.Address != frame.TemplateAddress || module.Session != frame.Session)
                    {
                        RemoteModule candidate = BuildRemote(frame, mount);
                        try { candidate.Binding.Validate(frame, Assets); candidate.Binding.Apply(frame, Assets); }
                        catch { candidate.Dispose(); throw; }
                        module?.Dispose(); module = candidate; standing[frame.Module] = module;
                    }
                    else
                    {
                        // Resolve and validate ALL properties/assets before changing visible content.
                        module.Binding.Validate(frame, Assets); module.Binding.Apply(frame, Assets);
                    }
                    Transform root = module.Binding.Root;
                    module.Host.transform.SetParent(mount, false);
                    if (frame.ParentModule != TownServiceFrame.ManifestModule
                        && frame.Nodes[0].Values.TryGetValue(TownServiceProperty.Sibling, out TownServiceValue? sibling))
                        module.Host.transform.SetSiblingIndex((int)sibling.Numbers[0]);
                    module.Host.GetComponent<CanvasGroup>().alpha = frame.ParentAlpha;
                    Transform poseRoot = module.AddedCanvas != null ? module.Host.transform : root;
                    poseRoot.position = mount.TransformPoint(Position(frame.Pose)); poseRoot.rotation = mount.rotation * Rotation(frame.Pose);
                    Vector3 worldScale = Vector3.Scale(mount.lossyScale, Scale(frame.Pose)), parentScale = poseRoot.parent.lossyScale;
                    poseRoot.localScale = new Vector3(worldScale.x / parentScale.x, worldScale.y / parentScale.y, worldScale.z / parentScale.z);
                    if (module.AddedCanvas != null)
                    {
                        if (root is RectTransform rr && module.Host.transform is RectTransform hostRect)
                        { hostRect.pivot = rr.pivot; hostRect.sizeDelta = rr.rect.size; }
                        root.localPosition = Vector3.zero; root.localRotation = Quaternion.identity; root.localScale = Vector3.one;
                    }
                    module.Sequence = frame.Sequence; module.Host.SetActive(true); root.gameObject.SetActive(true);
                }
                catch (Exception e) { if (module != null) module.Host.SetActive(false); Report("remote module " + entry.Key + "/" + frame.Module, e); }
            }
        }
    }

    private static RemoteModule BuildRemote(TownServiceFrame frame, Transform sharedFrame)
    {
        string key = TemplateKey(frame.Service, frame.Template, frame.TemplateAddress);
        if (!Templates.ContainsKey(key)) ResolveTemplate?.Invoke(frame.Service, frame.Template, frame.TemplateAddress);
        if (!Templates.TryGetValue(key, out GameObject? template) || template == null)
            throw new InvalidDataException("Original town-service template is not registered on this client.");
        var host = new GameObject("GVR town-service observer module", typeof(RectTransform));
        host.SetActive(false); host.transform.SetParent(sharedFrame, false);
        Canvas? canvas = null;
        if (frame.ParentModule == TownServiceFrame.ManifestModule && template.GetComponent<Canvas>() == null)
        {
            canvas = host.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Rig.VRRigDriver.HeadCamera != null ? Rig.VRRigDriver.HeadCamera : Camera.main;
        }
        CanvasGroup group = host.AddComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
        GameObject clone = Object.Instantiate(template, host.transform, false);
        // Templates are already inert; repeat the invariant before the clone can become active.
        TownServiceNeutralize.Apply(clone);
        return new RemoteModule { Host = host, AddedCanvas = canvas, Binding = new TownServiceBinding(clone.transform), Session = frame.Session, Template = frame.Template, Address = frame.TemplateAddress };
    }

    internal static void RemovePeer(int peer)
    { ClearRemoteModules(peer); Pending.Remove(peer); ReceivedBaselines.Remove(peer); Sessions.Remove(peer); }
    internal static void RequestFullRefresh()
    {
        foreach (LocalModule module in Local.Values)
        { module.Last = null; module.Baseline = null; module.NextRefresh = module.NextBaseline = 0; }
        _nextManifest = 0;
    }
    internal static void ResetNetwork()
    {
        foreach (int peer in new List<int>(Remote.Keys)) ClearRemoteModules(peer);
        Pending.Clear(); ReceivedBaselines.Clear(); Sessions.Clear(); foreach (LocalModule module in Local.Values)
        { module.Last = null; module.Baseline = null; module.NextRefresh = module.NextBaseline = 0; }
        _nextManifest = 0;
    }
    internal static void Shutdown()
    {
        ResetNetwork(); ClearLocalModules(); Templates.Clear();
        if (_templateHost != null) Object.Destroy(_templateHost);
        _templateHost = null; _session = 0; _service = 0; _active = false; _station = _sharedFrame = null;
        SourceParents.Clear(); ParentGroups.Clear(); TownServiceMaterial.Reset(); Assets.Clear();
        ReportReset();
    }
    private static void ClearLocalModules()
    { foreach (LocalModule module in Local.Values) module.Binding.Dispose(); Local.Clear(); }
    private static void ClearRemoteModules(int peer)
    { if (!Remote.TryGetValue(peer, out Dictionary<ushort, RemoteModule>? modules)) return;
        foreach (RemoteModule module in modules.Values) module.Dispose(); Remote.Remove(peer); }
    private static string TemplateKey(byte service, ushort template, string address = "")
    { if (service < 1 || service > 3 || template == 0) throw new ArgumentException("Invalid town-service template identity.");
        return service + ":" + (address.Length == 0 ? template.ToString() : address); }
    private static ulong NextSequence() { if (++_sequence == 0) ++_sequence; return _sequence; }
    private static float[] ReadPose(Transform source, Transform frame)
    {
        Vector3 p = frame.InverseTransformPoint(source.position), s = source.lossyScale, basis = frame.lossyScale;
        Quaternion q = Quaternion.Inverse(frame.rotation) * source.rotation;
        return new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x / basis.x, s.y / basis.y, s.z / basis.z };
    }
    private static float[] IdentityPose() => new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f };
    private static Vector3 Position(float[] pose) => new(pose[0], pose[1], pose[2]);
    private static Quaternion Rotation(float[] pose) => new(pose[3], pose[4], pose[5], pose[6]);
    private static Vector3 Scale(float[] pose) => new(pose[7], pose[8], pose[9]);
    private static bool SamePresentation(TownServiceFrame? a, TownServiceFrame b)
    {
        if (a == null || a.Visible != b.Visible || a.Structure != b.Structure || a.Nodes.Length != b.Nodes.Length
            || a.ParentModule != b.ParentModule || a.ParentBinding != b.ParentBinding || a.ParentAlpha != b.ParentAlpha) return false;
        for (int i = 0; i < 10; i++) if (a.Pose[i] != b.Pose[i]) return false;
        for (int i = 0; i < a.Nodes.Length; i++)
        {
            if (ReferenceEquals(a.Nodes[i], b.Nodes[i])) continue;
            if (a.Nodes[i].Binding != b.Nodes[i].Binding || a.Nodes[i].Values.Count != b.Nodes[i].Values.Count) return false;
            foreach (var pair in a.Nodes[i].Values)
                if (!b.Nodes[i].Values.TryGetValue(pair.Key, out TownServiceValue? value) || !pair.Value.Same(value)) return false;
        }
        return true;
    }
    private static void ReadParent(LocalModule module, TownServiceFrame frame)
    {
        float alpha = 1;
        for (Transform? parent = module.Binding.Root.parent; parent != null; parent = parent.parent)
        {
            if (SourceParents.TryGetValue(parent, out ParentLink link) && link.Module != module.Id)
            {
                frame.ParentModule = link.Module; frame.ParentBinding = link.Binding;
                frame.ParentAlpha = alpha; frame.Pose = ReadPose(module.Binding.Root, parent); return;
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
        Failures.Clear();
    }
    private static void Report(string phase, Exception error)
    {
        string key = phase + ": " + error.Message; float now = Time.unscaledTime;
        if (Failures.TryGetValue(key, out float last) && now - last < 30) return;
        if (Failures.Count > 32) Failures.Clear();
        Failures[key] = now;
        VRLog.Note("TownServices", "Original service presentation unavailable (" + key + ").");
        PresentationUnavailable?.Invoke(key);
    }
}
