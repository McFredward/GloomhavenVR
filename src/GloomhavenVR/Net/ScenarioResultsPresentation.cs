using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using TownServiceAssets = GloomhavenVR.Net.TownServices.TownServiceAssets;

namespace GloomhavenVR.Net;

/// <summary>Samples original results widgets and draws one inert, atomic public picture.
/// Native results/ready controllers remain alive; no received value invokes their callbacks.</summary>
internal sealed class ScenarioResultsPresentation
{
    private sealed class Source : IDisposable
    {
        internal ushort Id, Parent;
        internal uint ParentBinding;
        internal string Address = string.Empty;
        internal TownServiceBinding Binding = null!;
        public void Dispose() => Binding.Dispose();
    }
    private sealed class Copy : IDisposable
    {
        internal ScenarioResultsPart Part = null!;
        internal TownServiceBinding Binding = null!;
        public void Dispose() { Binding.Dispose(); if (Binding.Root != null) Object.Destroy(Binding.Root.gameObject); }
    }
    private readonly List<ScenarioResultsRoot> _roles = new();
    private readonly List<Source> _sources = new();
    private readonly HashSet<Transform> _cuts = new();
    private readonly List<Transform> _partition = new();
    private readonly Dictionary<Transform, Source> _sourceOwners = new();
    private readonly Dictionary<ushort, Copy> _copies = new();
    private readonly TownServiceAssets _assets = new();
    private readonly ScenarioResultsVisualLease _lease = new();
    private readonly ScenarioResultsVisualLease _closedLease = new();
    private Transform? _sourceWindow;
    private int _roleIdentity;
    private GameObject? _quarantine, _picture;
    private GameObject? _continuationPicture;
    private readonly List<ScenarioResultsContinuationProxy> _continuations = new(3);
    private GameObject? _closedAnchor;
    private UIWindow? _closedWindow;
    private int _closedAuthor;
    private uint _closedKey;
    private ConvertedPanel? _panel;
    private RectTransform? _previousFitRoot;
    private bool _previousFitEnabled;
    private Vector2 _previousFrame;
    private ScenarioResultsState? _pending, _displayed, _previous;
    private float _receivedAt, _blendDuration, _retryAt;
    private int _lastFrame = -1;
    private string _lastFailure = string.Empty;
    private float _nextFailure;

    /// <summary>The author samples original outputs after the native final visual writer.</summary>
    internal void Capture(ScenarioResultsState state)
    {
        if (!state.Visible) { state.Parts = Array.Empty<ScenarioResultsPart>(); return; }
        EnsureSources();
        ScenarioResultsPart[] parts;
        try { parts = ReadSources(); }
        catch (InvalidDataException) { ClearSources(); EnsureSources(); parts = ReadSources(); }
        state.Parts = parts;
        if (ScenarioResultsNative.TryGetConverted(out ConvertedPanel? panel) && panel != null)
            state.FrameSize = panel.HostRect.rect.size;
        else if (_sourceWindow is RectTransform rect) state.FrameSize = rect.rect.size;
    }

    /// <summary>Retain a whole owner-authored picture even if its local native template is late.</summary>
    internal void Apply(ScenarioResultsState state)
    {
        if (!state.HasPresentation) return;
        _retryAt = 0;
        if (state.Visible)
        {
            // A newly elected public author replaces the previous coordinator's
            // lifetime. Same-author future keys still wait for their native episode.
            if (_closedAuthor != 0 && (_closedAuthor != state.Author || _closedKey == state.Key)) ReleaseClosedLease();
            _pending = state;
            return;
        }
        // A received close can precede this client's native close/wait transition.
        // Keep that private output suppressed without retaining public picture ink
        // or interfering with the native controllers that finish the local episode.
        bool current = Matches(_displayed, state.Author, state.Key) || Matches(_pending, state.Author, state.Key)
            || _displayed == null && _pending == null;
        if (!current) return;
        if (_pending == null || Matches(_pending, state.Author, state.Key)) _pending = state;
        BeginClosedLease(state.Author, state.Key);
        RetirePicture();
        TickClosedLease();
    }

    /// <summary>A native closure retires only its own public opening. A complete
    /// future opening received before its local native template survives this edge.</summary>
    internal void RetireOpening(int author, uint key)
    {
        if (_displayed != null && _displayed.Author == author && _displayed.Key == key) RetirePicture();
        if (_pending != null && _pending.Author == author && _pending.Key == key) _pending = null;
        if (_closedAuthor == author && _closedKey == key) ReleaseClosedLease();
    }

    /// <summary>Called at Update and at the final visual boundary. Build at most once per frame;
    /// the cheap output lease is reasserted at both boundaries against native late writes.</summary>
    internal void Tick()
    {
        TickClosedLease();
        // An early packet for the next public opening cannot borrow the still-open
        // native window belonging to the preceding accepted close.
        if (_closedAnchor != null) return;
        if (_pending == null || !_pending.Visible)
        { _lease.Dispose(); return; }
        if (_lastFrame != Time.frameCount)
        {
            _lastFrame = Time.frameCount;
            if (Time.unscaledTime >= _retryAt)
            {
                try
                {
                    if (!ScenarioResultsNative.TryRead(out ScenarioResultsParts native)
                        || !native.Window.gameObject.activeInHierarchy || (!native.Window.IsOpen && !native.Window.IsVisible))
                        throw new InvalidDataException("The original results opening has not appeared yet.");
                    if (!ScenarioResultsNative.TryGetConverted(out ConvertedPanel? panel) || panel == null)
                        throw new InvalidDataException("The original results window has not converted yet.");
                    if (_panel != null && !ReferenceEquals(_panel, panel)) RetirePicture();
                    if (!ReferenceEquals(_displayed, _pending)) Accept(_pending, panel);
                    if (_picture != null && _displayed != null) Paint(_displayed);
                    _lastFailure = string.Empty;
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException)
                {
                    // A late original/asset never replaces a valid picture with a partial one.
                    _retryAt = Time.unscaledTime + .1f;
                    if (_lastFailure != ex.Message || Time.unscaledTime >= _nextFailure)
                    { _lastFailure = ex.Message; _nextFailure = Time.unscaledTime + 10f;
                        VRLog.Alert("Net", "Results picture is waiting for its original presentation: " + ex.Message); }
                }
            }
        }
        if (_picture != null && _panel != null && _panel.IsAlive)
        {
            UIWindow? native = ScenarioResultsNative.Window;
            _picture.SetActive(native != null && native.gameObject.activeInHierarchy && (native.IsOpen || native.IsVisible)
                && !_panel.RenderHidden && !_panel.OwnerRenderHidden);
            _lease.Tick(_picture.activeInHierarchy ? _picture.transform : null);
        }
        else { _lease.Dispose(); if (_picture != null) RetirePicture(); }
    }

    private void EnsureSources()
    {
        ScenarioResultsNative.CollectRoots(_roles);
        if (_roles.Count == 0) throw new InvalidDataException("Original results widgets are not initialized.");
        int identity = 17;
        foreach (ScenarioResultsRoot role in _roles) identity = unchecked(identity * 31 + role.Root.GetInstanceID());
        if (_sourceWindow == _roles[0].Root && identity == _roleIdentity && _sources.Count != 0) return;
        ClearSources(); _sourceWindow = _roles[0].Root; _roleIdentity = identity;
        foreach (ScenarioResultsRoot role in _roles) _cuts.Add(role.Root);
        // The game grows these two native collections independently on each client.
        // Publish each original row as its own module, retaining the exact prefab basis.
        foreach (UIScenarioBattleGoalResult row in _sourceWindow.GetComponentsInChildren<UIScenarioBattleGoalResult>(true)) _cuts.Add(row.transform);
        foreach (UIScenarioAchievement row in _sourceWindow.GetComponentsInChildren<UIScenarioAchievement>(true)) _cuts.Add(row.transform);
        foreach (ScenarioResultsRoot role in _roles) Partition(role.Root);
        if (_partition.Count > ScenarioResultsCodec.MaxParts) throw new InvalidDataException("Original results exceed the bounded module census.");
        for (int i = 0; i < _partition.Count; i++)
        {
            Transform root = _partition[i];
            var source = new Source { Id = (ushort)(i + 1), Address = Address(root),
                Binding = new TownServiceBinding(root, child => Excluded(child) || _cuts.Contains(child)) };
            _sources.Add(source);
            foreach (Transform node in source.Binding.Nodes) _sourceOwners[node] = source;
        }
        foreach (Source source in _sources)
        {
            if (source.Id == 1) continue;
            Transform? parent = source.Binding.Root.parent;
            if (parent != null && _sourceOwners.TryGetValue(parent, out Source? owner))
            { source.Parent = owner.Id; source.ParentBinding = owner.Binding.Bindings[Array.IndexOf(owner.Binding.Nodes, parent)]; }
            else { source.Parent = 1; source.ParentBinding = _sources[0].Binding.Bindings[0]; }
        }
    }

    private void Partition(Transform root)
    {
        if (_partition.Contains(root)) return;
        _partition.Add(root); _cuts.Add(root);
        if (Count(root, true) > TownServiceFrame.MaxNodes)
            for (int i = 0; i < root.childCount; i++)
            { Transform child = root.GetChild(i); if (!Excluded(child)) _cuts.Add(child); }
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i); if (Excluded(child)) continue;
            if (_cuts.Contains(child)) Partition(child);
            else DiscoverCuts(child);
        }
    }
    private void DiscoverCuts(Transform root)
    {
        for (int i = 0; i < root.childCount; i++)
        { Transform child = root.GetChild(i); if (Excluded(child)) continue;
            if (_cuts.Contains(child)) Partition(child); else DiscoverCuts(child); }
    }
    private int Count(Transform root, bool first)
    {
        if (!first && (Excluded(root) || _cuts.Contains(root))) return 0;
        int count = 1;
        for (int i = 0; i < root.childCount; i++) count += Count(root.GetChild(i), false);
        return count;
    }
    private static bool Excluded(Transform child) => TownServiceBinding.GeneratedTextMesh(child)
        || child.GetComponent<Camera>() != null
        // These are exact mod-owned presentation objects, never native widget names.
        || child.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal)
        || child.name.StartsWith("GVR ", StringComparison.Ordinal);

    private ScenarioResultsPart[] ReadSources()
    {
        var result = new ScenarioResultsPart[_sources.Count];
        for (int i = 0; i < _sources.Count; i++)
        {
            Source source = _sources[i]; TownServiceNode[] sampled = source.Binding.Read(_assets, true);
            var nodes = (TownServiceNode[])sampled.Clone();
            // Native ready is seated beside the original content. Its virtual parent
            // on the wire is the original root, not this client's temporary dock.
            if (source.Id != 1 && source.Parent == 1 && source.Binding.Root.parent != _sourceWindow)
            {
                Transform parent = _sources[0].Binding.Root;
                if (!source.Binding.Root.IsChildOf(parent)) nodes[0] = RelativeRoot(nodes[0], source.Binding.Root, parent);
            }
            result[i] = new ScenarioResultsPart { Id = source.Id, ParentId = source.Parent,
                ParentBinding = source.ParentBinding, Address = source.Address,
                Structure = source.Binding.Structure, Nodes = nodes };
        }
        return result;
    }

    private static TownServiceNode RelativeRoot(TownServiceNode node, Transform root, Transform parent)
    {
        var copy = CopyNode(node); var value = node.Values[TownServiceProperty.Transform];
        float[] n = (float[])value.Numbers.Clone(); Vector3 p = parent.InverseTransformPoint(root.position);
        Quaternion q = Quaternion.Inverse(parent.rotation) * root.rotation;
        Vector3 s = root.lossyScale, ps = parent.lossyScale;
        n[0] = p.x; n[1] = p.y; n[2] = p.z;
        n[3] = q.x; n[4] = q.y; n[5] = q.z; n[6] = q.w;
        n[7] = s.x / ps.x; n[8] = s.y / ps.y; n[9] = s.z / ps.z;
        if (n.Length == 18 && parent is RectTransform pr)
        { n[10] = n[12] = pr.pivot.x; n[11] = n[13] = pr.pivot.y; }
        copy.Values[TownServiceProperty.Transform] = new TownServiceValue { Numbers = n, Text = value.Text };
        // The separate dock follows the complete native window on the capture
        // canvas. Its child index0 is NOT a native-root sibling index: replaying
        // that index placed Ready before Background and a later Results pose
        // pushed Background in front of every row. Preserve the actual draw
        // order when mapping this detached footer into the virtual native root.
        if (node.Values.TryGetValue(TownServiceProperty.Sibling, out TownServiceValue? sibling))
            copy.Values[TownServiceProperty.Sibling] = new TownServiceValue
                { Numbers = new[] { (float)parent.childCount }, Text = sibling.Text };
        return copy;
    }

    private string Address(Transform root)
    {
        for (Transform? at = root; at != null; at = at.parent)
        {
            if (at.GetComponent<UIScenarioBattleGoalResult>() != null) return "b|" + Path(_sourceWindow!, at) + "|" + Path(at, root);
            if (at.GetComponent<UIScenarioAchievement>() != null) return "a|" + Path(_sourceWindow!, at) + "|" + Path(at, root);
            foreach (ScenarioResultsRoot role in _roles)
                if (role.Role == 6 && at == role.Root) return "r|" + Path(at, root);
            if (at == _sourceWindow) return "w|" + Path(at, root);
        }
        throw new InvalidDataException("Results module has no original identity.");
    }
    private static string Path(Transform parent, Transform node)
    {
        if (node == parent) return string.Empty;
        if (node.parent == null) throw new InvalidDataException("Results native path escaped its source.");
        int occurrence = 0;
        for (int i = 0; i < node.GetSiblingIndex(); i++) if (node.parent.GetChild(i).name == node.name) occurrence++;
        string head = Path(parent, node.parent);
        return head + "/" + Uri.EscapeDataString(node.name) + ":" + occurrence.ToString(CultureInfo.InvariantCulture);
    }
    private static Transform? Find(Transform? parent, string path)
    {
        foreach (string component in path.Split('/'))
        {
            if (component.Length == 0) continue;
            if (parent == null) return null;
            int colon = component.LastIndexOf(':'); if (colon < 0) return null;
            string name = Uri.UnescapeDataString(component.Substring(0, colon));
            if (!int.TryParse(component.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int occurrence)) return null;
            Transform? next = null;
            for (int i = 0; i < parent.childCount; i++)
            { Transform child = parent.GetChild(i); if (child.name == name && occurrence-- == 0) { next = child; break; } }
            parent = next;
        }
        return parent;
    }

    private Transform? Original(string address)
    {
        string[] parts = address.Split('|');
        if (parts.Length == 2 && parts[0] == "w") return Find(ScenarioResultsNative.Window?.transform, parts[1]);
        if (parts.Length == 2 && parts[0] == "r")
            return Find(Singleton<UIReadyToggle>.IsInitialized ? Singleton<UIReadyToggle>.Instance.transform : null, parts[1]);
        if (parts.Length != 3 || (parts[0] != "b" && parts[0] != "a")) return null;
        Transform? row = Find(ScenarioResultsNative.Window?.transform, parts[1]);
        Type type = parts[0] == "b" ? typeof(UIScenarioBattleGoalResult) : typeof(UIScenarioAchievement);
        if (row == null || row.GetComponent(type) == null)
        {
            UIResultsManager? manager = ScenarioResultsNative.Manager;
            FieldInfo? field = typeof(UINewAdventureResultsManager).GetField(parts[0] == "b" ? "battleGoalResultSlot" : "partyAchievementPrefab", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            row = manager != null ? (field?.GetValue(manager) as Component)?.transform : null;
        }
        return Find(row, parts[2]);
    }

    private void Accept(ScenarioResultsState state, ConvertedPanel panel)
    {
        bool same = _picture != null && _displayed != null && _displayed.Key == state.Key && _copies.Count == state.Parts.Length;
        if (same)
            foreach (ScenarioResultsPart part in state.Parts)
                if (!_copies.TryGetValue(part.Id, out Copy? copy) || copy.Part.Address != part.Address
                    || copy.Part.Structure != part.Structure || copy.Part.ParentId != part.ParentId || copy.Part.ParentBinding != part.ParentBinding)
                { same = false; break; }
        if (same)
            foreach (ScenarioResultsPart part in state.Parts) _copies[part.Id].Binding.Validate(Frame(part), _assets);
        else Build(state, panel);
        _previous = same ? _displayed : null;
        _blendDuration = _previous == null ? 0f : Mathf.Clamp(state.SampleTime - _previous.SampleTime, 0f, .25f);
        _receivedAt = Time.unscaledTime; _displayed = state;
        // Retire native footer geometry before recording the inert owner's
        // restoration receipt; an observer must never inherit a stranded fit lease.
        ScenarioResultsReadyPark.BeforeCommonPicture(panel);
        if (_panel == null)
        {
            _panel = panel; _previousFitEnabled = panel.FitEnabled; _previousFitRoot = panel.FitContentRoot;
            _previousFrame = panel.HostRect.rect.size;
        }
        panel.FitEnabled = false;
        panel.HostRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, state.FrameSize.x);
        panel.HostRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, state.FrameSize.y);
        panel.FitContentRoot = _picture!.transform as RectTransform;
        foreach (ScenarioResultsPart part in state.Parts) _copies[part.Id].Part = part;
    }

    private void Build(ScenarioResultsState state, ConvertedPanel panel)
    {
        if (_quarantine == null) { _quarantine = new GameObject("GloomhavenVR.Results.TemplateQuarantine"); _quarantine.SetActive(false); }
        var candidate = new GameObject("GloomhavenVR.Results.Observer", typeof(RectTransform));
        candidate.SetActive(false); candidate.transform.SetParent(_quarantine.transform, false);
        var copies = new Dictionary<ushort, Copy>();
        try
        {
            // Register every locally available original dependency before validating
            // any observer module, including a client-hidden native ready footer.
            EnsureSources();
            try { ReadSources(); }
            catch (InvalidDataException) { ClearSources(); EnsureSources(); ReadSources(); }
            var originals = new Dictionary<Transform, ushort>();
            foreach (ScenarioResultsPart part in state.Parts)
            {
                Transform original = Original(part.Address) ?? throw new InvalidDataException("Original results module is unavailable: " + part.Address);
                if (!originals.ContainsKey(original)) originals.Add(original, part.Id);
            }
            foreach (ScenarioResultsPart part in state.Parts)
            {
                Transform original = Original(part.Address)!;
                GameObject clone = Object.Instantiate(original.gameObject, candidate.transform, false);
                // Scroll/input paths use the exact original hierarchy identity.
                // Unity adds (Clone) to every separately instantiated module root.
                clone.name = original.name;
                Prune(original, clone.transform, originals);
                TownServiceNeutralize.Apply(clone);
                var binding = new TownServiceBinding(clone.transform);
                var copy = new Copy { Part = part, Binding = binding }; copies.Add(part.Id, copy);
                binding.Validate(Frame(part), _assets);
            }
            foreach (ScenarioResultsPart part in state.Parts)
            {
                Transform root = copies[part.Id].Binding.Root;
                if (part.Id == 1) continue;
                Copy parent = copies[part.ParentId]; int index = Array.IndexOf(parent.Binding.Bindings, part.ParentBinding);
                if (index < 0) throw new InvalidDataException("Results module lost its original parent.");
                root.SetParent(parent.Binding.Nodes[index], false);
            }
            foreach (Canvas canvas in candidate.GetComponentsInChildren<Canvas>(true))
            { canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = panel.HostCanvas.worldCamera; }
            SetLayer(candidate.transform, panel.Target.gameObject.layer);
            candidate.transform.SetParent(panel.HostRect, false);
            RectTransform mount = (RectTransform)candidate.transform;
            mount.anchorMin = mount.anchorMax = panel.HostRect.pivot; mount.pivot = panel.HostRect.pivot;
            mount.anchoredPosition3D = Vector3.zero; mount.localScale = Vector3.one; mount.localRotation = Quaternion.identity;
            mount.sizeDelta = state.FrameSize;
            // Material/widget application can still fail after shape validation.
            // Prepare the complete inactive candidate before retiring a valid picture;
            // neither the native output lease nor observers may see a partial stage.
            foreach (ScenarioResultsPart part in state.Parts)
            {
                Copy copy = copies[part.Id];
                copy.Binding.Apply(Frame(part), _assets);
                RootPose(copy.Binding.Root, part.Nodes[0]);
            }
            RetireCopies();
            _picture = candidate;
            foreach (var pair in copies) _copies.Add(pair.Key, pair.Value);
            panel.ExtraRenderRoots.Add(candidate.transform);
        }
        catch
        {
            foreach (Copy copy in copies.Values) copy.Binding.Dispose();
            Object.Destroy(candidate); throw;
        }
    }
    private void Prune(Transform source, Transform copy, Dictionary<Transform, ushort> roots)
    {
        for (int i = source.childCount - 1; i >= 0; i--)
        {
            Transform child = source.GetChild(i), duplicate = copy.GetChild(i);
            if (Excluded(child) || roots.ContainsKey(child) || _cuts.Contains(child)) Object.DestroyImmediate(duplicate.gameObject);
            else Prune(child, duplicate, roots);
        }
    }
    private static void SetLayer(Transform root, int layer)
    { root.gameObject.layer = layer; for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i), layer); }

    private void Paint(ScenarioResultsState state)
    {
        float amount = _blendDuration <= 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - _receivedAt) / _blendDuration);
        foreach (ScenarioResultsPart part in state.Parts)
        {
            Copy copy = _copies[part.Id]; ScenarioResultsPart? before = null;
            if (_previous != null) foreach (ScenarioResultsPart candidate in _previous.Parts) if (candidate.Id == part.Id) { before = candidate; break; }
            TownServiceNode[] nodes = before == null || amount >= 1f ? part.Nodes : Blend(before.Nodes, part.Nodes, amount);
            var frame = new TownServiceFrame { Structure = part.Structure, Nodes = nodes };
            copy.Binding.Apply(frame, _assets); RootPose(copy.Binding.Root, nodes[0]);
        }
        AttachContinuationInputs(state);
        if (_copies.TryGetValue(1, out Copy? rootCopy)) SharedWindowScrollSync.AttachResultsInputs(rootCopy.Binding.Root, _panel?.HostCanvas);
        if (_picture != null) _picture.SetActive(true);
    }
    private void AttachContinuationInputs(ScenarioResultsState state)
    {
        if (_picture == null) return;
        if (_continuationPicture == _picture)
        { foreach (ScenarioResultsContinuationProxy current in _continuations) current.Refresh(state.Key); return; }
        _continuations.Clear(); _continuationPicture = _picture;
        for (byte operation = ScenarioResultsNative.ContinueReady; operation <= ScenarioResultsNative.RetryScenario; operation++)
        {
            Selectable? original = ScenarioResultsNative.ContinuationTarget(operation);
            if (original == null) continue;
            foreach (Copy copy in _copies.Values)
            {
                Transform? basis = Original(copy.Part.Address);
                if (basis == null || original.transform != basis && !original.transform.IsChildOf(basis)) continue;
                Transform? target = Find(copy.Binding.Root, Path(basis, original.transform));
                if (!(target is RectTransform frame)) continue;
                ScenarioResultsContinuationProxy? proxy = frame.GetComponentInChildren<ScenarioResultsContinuationProxy>(true);
                if (proxy == null)
                {
                    var go = new GameObject("GloomhavenVR.ResultsContinuationInput", typeof(RectTransform), typeof(CanvasRenderer),
                        typeof(CanvasGroup), typeof(Image), typeof(ScenarioResultsContinuationProxy));
                    var rect = (RectTransform)go.transform; rect.SetParent(frame, false);
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                    go.layer = frame.gameObject.layer;
                    Image ink = go.GetComponent<Image>(); ink.color = Color.clear; ink.raycastTarget = true;
                    CanvasGroup group = go.GetComponent<CanvasGroup>(); group.ignoreParentGroups = true;
                    group.interactable = group.blocksRaycasts = true;
                    proxy = go.GetComponent<ScenarioResultsContinuationProxy>();
                }
                proxy.Operation = operation;
                proxy.NativeGroups = frame.GetComponentsInParent<CanvasGroup>(true);
                proxy.Refresh(state.Key); _continuations.Add(proxy);
                if (_panel != null)
                {
                    SharedWindowScrollCanvasLease? lease = _picture.GetComponent<SharedWindowScrollCanvasLease>();
                    if (lease == null) lease = _picture.AddComponent<SharedWindowScrollCanvasLease>();
                    lease.Admit(_panel.HostCanvas, proxy.GetComponentInParent<Canvas>(true));
                }
                break;
            }
        }
    }
    private static void RootPose(Transform root, TownServiceNode node)
    {
        float[] n = node.Values[TownServiceProperty.Transform].Numbers;
        if (root is RectTransform rect) rect.anchoredPosition3D = new Vector3(n[0], n[1], n[2]);
        else root.localPosition = new Vector3(n[0], n[1], n[2]);
        root.localRotation = new Quaternion(n[3], n[4], n[5], n[6]); root.localScale = new Vector3(n[7], n[8], n[9]);
        root.gameObject.SetActive(node.Values[TownServiceProperty.Active].Numbers[0] != 0f);
        if (node.Values.TryGetValue(TownServiceProperty.Sibling, out TownServiceValue? sibling)) root.SetSiblingIndex((int)sibling.Numbers[0]);
    }
    private static TownServiceNode[] Blend(TownServiceNode[] before, TownServiceNode[] after, float amount)
    {
        var result = new TownServiceNode[after.Length];
        for (int i = 0; i < result.Length; i++)
        {
            TownServiceNode node = after[i]; result[i] = node;
            if (i >= before.Length || before[i].Binding != node.Binding) continue;
            foreach (var property in node.Values)
            {
                TownServiceValue value = property.Value;
                if (!before[i].Values.TryGetValue(property.Key, out TownServiceValue? old) || old.Numbers.Length != value.Numbers.Length
                    || !SameText(old.Text, value.Text)) continue;
                float[]? n = null;
                for (int j = 0; j < value.Numbers.Length; j++)
                    if (Continuous(property.Key, j) && old.Numbers[j] != value.Numbers[j])
                    { n ??= (float[])value.Numbers.Clone(); n[j] = Mathf.LerpUnclamped(old.Numbers[j], value.Numbers[j], amount); }
                if (n == null) continue;
                if (property.Key == TownServiceProperty.Transform)
                {
                    Quaternion q = Quaternion.SlerpUnclamped(new Quaternion(old.Numbers[3], old.Numbers[4], old.Numbers[5], old.Numbers[6]),
                        new Quaternion(value.Numbers[3], value.Numbers[4], value.Numbers[5], value.Numbers[6]), amount);
                    n[3] = q.x; n[4] = q.y; n[5] = q.z; n[6] = q.w;
                }
                if (ReferenceEquals(result[i], node)) result[i] = CopyNode(node);
                result[i].Values[property.Key] = new TownServiceValue { Numbers = n, Text = value.Text };
            }
        }
        return result;
    }
    private static bool Continuous(ushort property, int index) => property switch
    {
        TownServiceProperty.Transform => true,
        TownServiceProperty.Graphic => index > 0,
        TownServiceProperty.Renderer => true,
        TownServiceProperty.Group => index == 1,
        TownServiceProperty.Image => index == 1,
        TownServiceProperty.RawImage => true,
        TownServiceProperty.TmpText => index == 0 || index >= 8 && index <= 17 || index >= 24,
        TownServiceProperty.LegacyText => index == 0,
        TownServiceProperty.RectMask => index >= 1 && index <= 4,
        TownServiceProperty.Shadow or TownServiceProperty.Outline => index > 0 && index < 7,
        // Keep queue/instancing/type tags discrete; interpolate only the native
        // named material's colour/vector/float/texture-offset output values.
        _ => (property == TownServiceProperty.Material || property == TownServiceProperty.TextMaterial
                || property >= TownServiceProperty.MeshMaterial0 && property <= TownServiceProperty.MeshMaterial7)
            && index >= 4 && (index - 4) % 5 != 0
    };
    private static bool SameText(string[] a, string[] b)
    { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    private static TownServiceNode CopyNode(TownServiceNode node)
    { var copy = new TownServiceNode { Binding = node.Binding }; foreach (var p in node.Values) copy.Values.Add(p.Key, p.Value); return copy; }
    private static TownServiceFrame Frame(ScenarioResultsPart part) => new() { Structure = part.Structure, Nodes = part.Nodes };

    private static bool Matches(ScenarioResultsState? state, int author, uint key)
        => state != null && state.Author == author && state.Key == key;

    private void BeginClosedLease(int author, uint key)
    {
        UIWindow? window = ScenarioResultsNative.Window;
        if (window == null || !window.gameObject.activeInHierarchy || (!window.IsOpen && !window.IsVisible)) return;
        if (_closedAuthor == author && _closedKey == key && _closedWindow == window && _closedAnchor != null) return;
        ReleaseClosedLease();
        _closedAuthor = author; _closedKey = key; _closedWindow = window;
        // No Canvas, Graphic or Renderer: this is only a lifetime/lease identity.
        // It is outside the native target, so its controllers remain untouched.
        _closedAnchor = new GameObject("GloomhavenVR.Results.AcceptedClose");
    }

    private void TickClosedLease()
    {
        if (_closedAnchor == null) return;
        if (_closedWindow == null || ScenarioResultsNative.Window != _closedWindow
            || !_closedWindow.gameObject.activeInHierarchy || (!_closedWindow.IsOpen && !_closedWindow.IsVisible))
        { ReleaseClosedLease(); return; }
        _closedLease.Tick(_closedAnchor.transform);
    }

    private void ReleaseClosedLease()
    {
        _closedLease.Dispose();
        if (_closedAnchor != null) { _closedAnchor.SetActive(false); Object.Destroy(_closedAnchor); }
        _closedAnchor = null; _closedWindow = null; _closedAuthor = 0; _closedKey = 0;
    }

    private void RetireCopies()
    {
        _lease.Dispose();
        _continuations.Clear(); _continuationPicture = null;
        if (_panel != null && _picture != null) _panel.ExtraRenderRoots.Remove(_picture.transform);
        foreach (Copy copy in _copies.Values) copy.Binding.Dispose(); _copies.Clear();
        if (_picture != null) { _picture.SetActive(false); Object.Destroy(_picture); } _picture = null;
    }
    private void RetirePicture()
    {
        RetireCopies();
        if (_panel != null && _panel.IsAlive)
        {
            _panel.FitEnabled = _previousFitEnabled; _panel.FitContentRoot = _previousFitRoot;
            _panel.HostRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _previousFrame.x);
            _panel.HostRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _previousFrame.y);
        }
        _panel = null; _displayed = _previous = null;
    }
    private void ClearSources()
    {
        foreach (Source source in _sources) source.Dispose(); _sources.Clear();
        _cuts.Clear(); _partition.Clear(); _sourceOwners.Clear(); _sourceWindow = null; _roleIdentity = 0;
    }
    internal void Reset()
    {
        RetirePicture(); ReleaseClosedLease(); ClearSources(); _assets.Clear(); _roles.Clear(); _pending = null;
        _lastFrame = -1; _retryAt = 0; _lastFailure = string.Empty;
        if (_quarantine != null) Object.Destroy(_quarantine); _quarantine = null;
    }
}
