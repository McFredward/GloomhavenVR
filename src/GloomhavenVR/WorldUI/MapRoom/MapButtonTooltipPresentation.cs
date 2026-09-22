using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Net;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>Original map-hover label and native disabled explanation beside the physical cap.
/// Peers receive bounded rendered geometry and text, never a hover that executes their HUD.
/// Original tooltip line construction is presentation-only; cloned controllers never activate.</summary>
internal static class MapButtonTooltipPresentation
{
    internal const int MaxPayload = 16384;
    private const int MaxNodes = 48;
    private static Component? _button;
    private static Transform? _anchor;
    private static float _scale;
    private static byte _key;
    private static UITextTooltipTarget? _explanation;
    private static TMP_Text? _titleSource;
    private static Vector3 _titleNativePosition;
    private static UITooltip? _detailSource;
    private static Surface? _title;
    private static byte[]? _last;
    private static bool _refused;
    private static readonly MemoryStream SendBuffer = new(MaxPayload);
    private static readonly BinaryWriter SendWriter = new(SendBuffer);
    private static readonly Dictionary<RectTransform, SampleCache> Samples = new();
    private static readonly Dictionary<string, TMP_SpriteAsset> SpriteAssets = new();
    private static readonly Dictionary<int, Peer> Peers = new();
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly MethodInfo? BuildLines = typeof(UITooltip).GetMethod("EvaluateAndCreateTooltipLines", Fields);

    internal static void SetLocalHover(Component? button, Transform? anchor, float scale)
    {
        if (ReferenceEquals(button, _button))
        {
            if (button != null) { _anchor = anchor; _scale = scale; UpdateTitle(); }
            else if (_detailSource == null || !_detailSource.IsActive() || _detailSource.alpha <= .001f)
            { _anchor = null; _explanation = null; _detailSource = null; _last = null; }
            return;
        }
        if (_explanation != null) _explanation.OnPointerExit(null);
        if (button == null)
        {
            if (Singleton<UIGuildmasterHUD>.IsInitialized) Singleton<UIGuildmasterHUD>.Instance.OnHovered(false);
            _button = null; _title?.Show(false);
            // UITooltipTarget clears TooltipShown at hide request, before its native fade ends.
            // Keep the last cap's seat and publish the actual fade until the pixels disappear.
            if (_detailSource == null || !_detailSource.IsActive() || _detailSource.alpha <= .001f)
            { _anchor = null; _explanation = null; _detailSource = null; _last = null; }
            return;
        }
        _button = button; _anchor = anchor; _scale = scale; _detailSource = null;
        _explanation = null; _key = 0;
        if (button != null && Singleton<UIGuildmasterHUD>.IsInitialized)
        {
            // Native label + hover-animation API only. Disabled/inactive Toggles can suppress
            // their pointer preview callback, but physical caps still expose their explanation.
            Singleton<UIGuildmasterHUD>.Instance.OnHovered(true);
            _key = button is UICityEncounterButton ? (byte)255 : (byte)(1 + (int)((UIGuildmasterButton)button).GuildmasterMode);
            _titleSource = MapCityEventSource.Field<TextLocalizedListener>(Singleton<UIGuildmasterHUD>.Instance,
                "hoveredOptionTooltip")?.Text;
            string field = button is UICityEncounterButton ? "tooltip" : "tooltipTarget";
            _explanation = MapCityEventSource.Field<UITextTooltipTarget>(button, field);
            if (_explanation != null && _explanation.gameObject.activeSelf && _explanation.CanBeShown)
            {
                var pointer = new PointerEventData(EventSystem.current) { pointerId = UguiPointer.ModPointerIdCeiling,
                    pointerEnter = _explanation.gameObject };
                _explanation.OnPointerEnter(pointer);
            }
            // Native pointer events cannot reach a source whose ancestor bar is hidden. Only its
            // presentation label is selected here; mode/gameplay and native tooltip text are untouched.
            if (_titleSource != null)
            {
                string key = button is UICityEncounterButton ? "GUI_CITY_ENCOUNTER"
                    : "GUI_GUILD_" + ((UIGuildmasterButton)button).GuildmasterMode;
                MapCityEventSource.Field<TextLocalizedListener>(Singleton<UIGuildmasterHUD>.Instance,
                    "hoveredOptionTooltip")?.SetTextKey(key);
                Canvas? canvas = _titleSource.GetComponentInParent<Canvas>();
                _titleNativePosition = canvas != null ? canvas.transform.InverseTransformPoint(_titleSource.transform.position)
                    : _titleSource.rectTransform.localPosition;
            }
        }
        UpdateTitle();
    }
    internal static void ClearLocal()
    {
        if (_button != null && Singleton<UIGuildmasterHUD>.IsInitialized) Singleton<UIGuildmasterHUD>.Instance.OnHovered(false);
        if (_explanation != null) _explanation.OnPointerExit(null);
        _button = null; _anchor = null; _explanation = null; _detailSource = null; _last = null;
        _title?.Destroy(); _title = null; _titleSource = null; Samples.Clear(); _refused = false;
    }

    private static void UpdateTitle()
    {
        if (_button == null || _anchor == null || _titleSource == null)
        { _title?.Show(false); _last = null; return; }
        try
        {
            _title ??= new Surface();
            Node[] nodes = SampleNodes(_titleSource.rectTransform);
            if (nodes.Length == 0) return;
            Node root = nodes[0];
            root.Rect[0] = root.Rect[1] = root.Rect[2] = root.Rect[3] = .5f;
            root.Rect[4] = root.Rect[5] = .5f;
            root.Rect[6] = _titleSource.rectTransform.rect.width;
            root.Rect[7] = _titleSource.rectTransform.rect.height;
            root.Rect[8] = root.Rect[9] = root.Rect[10] = 0f;
            // Detached title keeps the native font, shape and animation scale; only its seat is VR.
            root.Active = true;
            root.Alpha *= ParentAlpha(_titleSource.transform.parent);
            Canvas? nativeCanvas = _titleSource.GetComponentInParent<Canvas>();
            Transform? frame = nativeCanvas != null ? nativeCanvas.transform : null;
            Vector3 nativeScale = frame != null ? frame.lossyScale : Vector3.one;
            Vector3 actualScale = _titleSource.transform.lossyScale;
            Vector3 relativeScale = new(actualScale.x / Mathf.Max(.000001f, Mathf.Abs(nativeScale.x)),
                actualScale.y / Mathf.Max(.000001f, Mathf.Abs(nativeScale.y)), actualScale.z / Mathf.Max(.000001f, Mathf.Abs(nativeScale.z)));
            Vector3 nativePosition = frame != null ? frame.InverseTransformPoint(_titleSource.transform.position) : _titleSource.transform.localPosition;
            Vector3 motion = nativePosition - _titleNativePosition;
            Picture p = new() { Key = _key, Detail = false, Nodes = nodes,
                Position = _anchor.position + _anchor.right * ((.19f + motion.x * .00065f) * _scale)
                    + _anchor.up * (motion.y * .00065f * _scale) - _anchor.forward * (.013f * _scale),
                Rotation = _anchor.rotation * (frame != null ? Quaternion.Inverse(frame.rotation) * _titleSource.transform.rotation
                    : _titleSource.transform.localRotation), Scale = relativeScale * (.00065f * _scale) };
            _title.Paint(p, _titleSource.rectTransform, null);
        }
        catch (Exception e) { Report(e); _title?.Show(false); }
    }

    /// <summary>Called after WorldTooltips' normal positioning, so a cap owns only its own hint.</summary>
    internal static bool PlaceNativeExplanation(UITooltip? tooltip, Canvas canvas)
    {
        if (_anchor == null || _explanation == null || tooltip == null || !tooltip.IsActive() || tooltip.alpha <= .001f
            || !ReferenceEquals(tooltip.m_AnchorToTarget, _explanation.transform)) return false;
        _detailSource = tooltip;
        canvas.transform.rotation = _anchor.rotation;
        canvas.transform.localScale = Vector3.one * (.00065f * _scale);
        RectTransform rect = (RectTransform)tooltip.transform;
        Vector3 desired = _anchor.position + _anchor.right * (.19f * _scale)
            - _anchor.up * (.070f * _scale) - _anchor.forward * (.014f * _scale);
        rect.position += desired - rect.TransformPoint(rect.rect.center);
        return true;
    }

    internal static byte[]? Capture()
    {
        if (_anchor == null || _title?.Root == null || !MapRoomDriver.TryGetParchmentFrame(out Vector3 center, out float scale))
        { _last = null; return null; }
        try
        {
            var pictures = new List<Picture>(2);
            pictures.Add(SamplePicture(_title.Root, false, center, scale));
            if (_detailSource != null && _explanation != null && _detailSource.IsActive() && _detailSource.alpha > .001f)
                pictures.Add(SamplePicture((RectTransform)_detailSource.transform, true, center, scale));
            MemoryStream stream = SendBuffer; BinaryWriter writer = SendWriter; stream.SetLength(0); stream.Position = 0;
            writer.Write((byte)1); writer.Write(_key); writer.Write((byte)pictures.Count);
            foreach (Picture picture in pictures) Write(writer, picture);
            if (stream.Length > MaxPayload) throw new InvalidDataException("map tooltip exceeds bounded snapshot");
            byte[] buffer = stream.GetBuffer();
            if (_last != null && Same(_last, buffer, (int)stream.Length)) return _last;
            byte[] bytes = stream.ToArray();
            _last = bytes; return bytes;
        }
        catch (Exception e) { Report(e); _last = null; return null; }
    }

    internal static void Receive(int sender, byte[]? payload, float sampleTime)
    {
        Picture[] pictures = Array.Empty<Picture>();
        if (payload != null && !TryRead(payload, out pictures)) return;
        if (!Peers.TryGetValue(sender, out Peer? peer)) Peers[sender] = peer = new Peer();
        if (sampleTime <= peer.SampleTime) return;
        if (peer.History.Count == 0)
        { peer.History.Clear(); peer.Clock.Reset(sampleTime, Time.unscaledTime); }
        peer.SampleTime = sampleTime; peer.Pictures = pictures;
        if (peer.History.Count == 32) peer.History.RemoveAt(0);
        peer.History.Add((sampleTime, pictures));
    }
    internal static void Tick()
    {
        if (!MapRoomDriver.TryGetParchmentFrame(out Vector3 center, out float scale))
        { foreach (Peer peer in Peers.Values) peer.Hide(); return; }
        foreach (Peer peer in Peers.Values)
        {
            // Session/room lifecycle and an explicit clear own visibility. A stalled packet is
            // not proof that a still-hovered hint disappeared on its owner.
            float cursor = peer.Clock.Advance(Time.unscaledTime, peer.SampleTime);
            var from = peer.History[0]; var to = peer.History[peer.History.Count - 1];
            foreach (var sample in peer.History)
            { if (sample.Time <= cursor) from = sample; if (sample.Time >= cursor) { to = sample; break; } }
            float progress = peer.Clock.Progress(from.Time, to.Time);
            Picture[] shown = peer.Clock.Cursor >= to.Time ? to.Pictures : from.Pictures;
            for (int i = 0; i < peer.Surfaces.Length; i++)
            {
                if (i >= shown.Length) { peer.Surfaces[i].Show(false); continue; }
                Picture p = shown[i]; Picture previous = p;
                if (i < from.Pictures.Length && i < to.Pictures.Length && Compatible(from.Pictures[i], to.Pictures[i]))
                { previous = from.Pictures[i]; p = to.Pictures[i]; }
                RectTransform? source = ResolveSource(p.Detail);
                if (source == null) { peer.Surfaces[i].Show(false); continue; }
                try { peer.Surfaces[i].Paint(p, source, p.Detail ? p.Lines : null, previous, progress, center, scale); }
                catch (Exception e) { Report(e); peer.Surfaces[i].Show(false); }
            }
        }
    }
    private static bool Compatible(Picture a, Picture b)
    {
        if (a.Key != b.Key || a.Detail != b.Detail || a.Nodes.Length != b.Nodes.Length) return false;
        for (int i = 0; i < a.Nodes.Length; i++)
            if (a.Nodes[i].Kind != b.Nodes[i].Kind || a.Nodes[i].Text != b.Nodes[i].Text) return false;
        return true;
    }
    internal static void Remove(int player) { if (Peers.TryGetValue(player, out Peer? p)) p.Destroy(); Peers.Remove(player); }
    internal static void Reset()
    {
        ClearLocal(); _title?.Destroy(); _title = null;
        foreach (Peer p in Peers.Values) p.Destroy(); Peers.Clear(); Samples.Clear(); SpriteAssets.Clear(); _last = null; _titleSource = null; _refused = false;
    }

    private static RectTransform? ResolveSource(bool detail)
    {
        if (!detail) return Singleton<UIGuildmasterHUD>.IsInitialized
            ? MapCityEventSource.Field<TextLocalizedListener>(Singleton<UIGuildmasterHUD>.Instance, "hoveredOptionTooltip")?.Text.rectTransform : null;
        // The native singleton may be inactive while a peer reads its hint. It is a template only.
        UITooltip? tip = typeof(UITooltip).GetField("mInstance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as UITooltip;
        return tip != null ? (RectTransform)tip.transform : null;
    }

    private sealed class Peer
    {
        internal Picture[] Pictures = Array.Empty<Picture>();
        internal readonly Surface[] Surfaces = { new(), new() };
        internal float SampleTime = -1;
        internal readonly List<(float Time, Picture[] Pictures)> History = new(32);
        internal readonly UseBarAnimationPlaybackClock Clock = new();
        internal void Hide() { foreach (Surface s in Surfaces) s.Show(false); }
        internal void Destroy() { foreach (Surface s in Surfaces) s.Destroy(); }
    }
    private sealed class Picture
    {
        internal byte Key; internal bool Detail; internal Vector3 Position, Scale; internal Quaternion Rotation;
        internal Node[] Nodes = Array.Empty<Node>(); internal string[] Lines = Array.Empty<string>();
    }
    private sealed class Node
    {
        internal float[] Rect = Array.Empty<float>(); internal bool Active, Enabled;
        internal float Alpha = 1f; internal Color Color = Color.white, RendererColor = Color.white;
        internal string Text = string.Empty; internal byte Kind; internal float[] TextStyle = Array.Empty<float>();
    }
    private static Picture SamplePicture(RectTransform root, bool detail, Vector3 center, float scale)
    {
        var p = new Picture { Key = _key, Detail = detail, Position = (root.position - center) / scale,
            Rotation = root.rotation, Scale = root.lossyScale / scale, Nodes = SampleNodes(root) };
        p.Nodes[0].Rect[0] = p.Nodes[0].Rect[1] = p.Nodes[0].Rect[2] = p.Nodes[0].Rect[3] = .5f;
        p.Nodes[0].Rect[6] = root.rect.width; p.Nodes[0].Rect[7] = root.rect.height;
        p.Nodes[0].Active = root.gameObject.activeInHierarchy;
        if (detail && _detailSource?.m_LinesTemplate != null)
        {
            var lines = new List<string>();
            foreach (UITooltipLines.Line line in _detailSource.m_LinesTemplate.lineList)
            { lines.Add(((int)line.style).ToString(System.Globalization.CultureInfo.InvariantCulture)); lines.Add(line.left ?? ""); lines.Add(line.right ?? ""); lines.Add(line.spriteAsset != null ? line.spriteAsset.name : ""); }
            p.Lines = lines.ToArray();
        }
        return p;
    }
    private static RectTransform[] Rects(RectTransform root)
    {
        var list = new List<RectTransform>();
        foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
            if (rect.GetComponent<TMP_SubMeshUI>() == null) list.Add(rect);
        if (list.Count > MaxNodes) throw new InvalidDataException("map tooltip hierarchy exceeds node budget");
        return list.ToArray();
    }
    private static Node[] SampleNodes(RectTransform root)
    {
        if (!Samples.TryGetValue(root, out SampleCache? cache)) Samples[root] = cache = new();
        cache.Rects.Clear(); root.GetComponentsInChildren(true, cache.Rects);
        cache.Rects.RemoveAll(IsSubmesh);
        if (cache.Rects.Count > MaxNodes) throw new InvalidDataException("map tooltip hierarchy exceeds node budget");
        if (cache.Nodes.Length != cache.Rects.Count)
        {
            cache.Nodes = new Node[cache.Rects.Count];
            for (int i = 0; i < cache.Nodes.Length; i++) cache.Nodes[i] = new Node { Rect = new float[18], TextStyle = new float[18] };
        }
        List<RectTransform> rects = cache.Rects; Node[] nodes = cache.Nodes;
        for (int i = 0; i < rects.Count; i++)
        {
            RectTransform r = rects[i]; Graphic? g = r.GetComponent<Graphic>(); TMP_Text? t = g as TMP_Text;
            Node n = nodes[i]; FillRect(r, n.Rect); n.Active = r.gameObject.activeSelf;
            n.Alpha = r.GetComponent<CanvasGroup>()?.alpha ?? 1f; n.Kind = (byte)(t != null ? 2 : g != null ? 1 : 0);
            n.Enabled = g != null && g.enabled; n.Color = g != null ? g.color : UnityEngine.Color.white;
            n.RendererColor = g != null ? g.canvasRenderer.GetColor() : UnityEngine.Color.white;
            if (t != null)
            {
                n.Text = t.text; float[] s = n.TextStyle;
                s[0] = t.fontSize; s[1] = t.fontSizeMin; s[2] = t.fontSizeMax; s[3] = t.characterSpacing; s[4] = t.wordSpacing;
                s[5] = t.lineSpacing; s[6] = t.paragraphSpacing; s[7] = (float)t.fontWeight; s[8] = (float)t.fontStyle;
                s[9] = (float)t.alignment; s[10] = (float)t.overflowMode; s[11] = t.enableAutoSizing ? 1f : 0f;
                s[12] = t.enableWordWrapping ? 1f : 0f; s[13] = t.richText ? 1f : 0f;
                s[14] = t.margin.x; s[15] = t.margin.y; s[16] = t.margin.z; s[17] = t.margin.w;
            }
            nodes[i] = n;
        }
        return nodes;
    }
    private sealed class SampleCache { internal readonly List<RectTransform> Rects = new(); internal Node[] Nodes = Array.Empty<Node>(); }
    private static bool IsSubmesh(RectTransform r) => r.GetComponent<TMP_SubMeshUI>() != null;
    private static void FillRect(RectTransform r, float[] a)
    {
        a[0] = r.anchorMin.x; a[1] = r.anchorMin.y; a[2] = r.anchorMax.x; a[3] = r.anchorMax.y;
        a[4] = r.pivot.x; a[5] = r.pivot.y; a[6] = r.sizeDelta.x; a[7] = r.sizeDelta.y;
        a[8] = r.anchoredPosition3D.x; a[9] = r.anchoredPosition3D.y; a[10] = r.anchoredPosition3D.z;
        a[11] = r.localScale.x; a[12] = r.localScale.y; a[13] = r.localScale.z;
        a[14] = r.localRotation.x; a[15] = r.localRotation.y; a[16] = r.localRotation.z; a[17] = r.localRotation.w;
    }

    private sealed class Surface
    {
        private GameObject? _host; internal RectTransform? Root;
        private RectTransform? _source; private RectTransform[] _nodes = Array.Empty<RectTransform>();
        private string _recipe = string.Empty;
        internal void Show(bool visible) { if (_host != null && _host.activeSelf != visible) _host.SetActive(visible); }
        internal void Destroy()
        { if (Root != null) Samples.Remove(Root); if (_host != null) { _host.SetActive(false); Object.Destroy(_host); } _host = null; Root = null; _source = null; }
        internal void Paint(Picture p, RectTransform source, string[]? lines, Picture? previous = null,
            float progress = 1f, Vector3 center = default, float mapScale = 1f)
        {
            string recipe = lines != null ? string.Join("\0", lines) : "";
            if (Root == null || !ReferenceEquals(_source, source) || _nodes.Length != p.Nodes.Length || recipe != _recipe)
            {
                Destroy(); _host = new GameObject("GloomhavenVR.NativeMapButtonTooltip", typeof(RectTransform)); _host.SetActive(false);
                Canvas canvas = _host.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = Rig.VRRigDriver.HeadCamera; canvas.sortingOrder = 105;
                GameObject clone = Object.Instantiate(source.gameObject, _host.transform, false);
                Root = (RectTransform)clone.transform;
                if (lines != null)
                {
                    UITooltip tip = clone.GetComponent<UITooltip>();
                    if (tip == null || BuildLines == null) throw new InvalidOperationException("native tooltip line recipe unavailable");
                    for (int i = Root.childCount - 1; i >= 0; i--)
                        if (Root.GetChild(i).name == "Line") Object.DestroyImmediate(Root.GetChild(i).gameObject);
                    var template = new UITooltipLines();
                    for (int i = 0; i < lines.Length; i += 4)
                        template.AddLine(lines[i + 1], lines[i + 2], new RectOffset(),
                            (UITooltipLines.LineStyle)int.Parse(lines[i], System.Globalization.CultureInfo.InvariantCulture), ResolveSpriteAsset(lines[i + 3]));
                    typeof(UITooltip).GetField("m_LinesTemplate", Fields)!.SetValue(tip, template);
                    BuildLines.Invoke(tip, null);
                }
                RemoteWidgetMirror.Neutralize(clone, RemoteWidgetMirror.LayoutOwner.Source, null);
                foreach (Transform node in _host.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = VRLayers.ModLayer;
                _nodes = Rects(Root); _source = source; _recipe = recipe;
                if (_nodes.Length != p.Nodes.Length) throw new InvalidOperationException("native tooltip hierarchy differs from sender");
            }
            if (previous == null || previous.Key != p.Key || previous.Nodes.Length != p.Nodes.Length) { previous = p; progress = 1f; }
            for (int i = 0; i < _nodes.Length; i++) Apply(_nodes[i], p.Nodes[i], previous.Nodes[i], progress);
            Root.anchorMin = Root.anchorMax = new Vector2(.5f, .5f);
            ApplyRootPose(Root, p, previous, progress, center, mapScale);
            // Owner root position/scale are absolute, not its native parent's screen-space anchors.
            _host!.transform.localScale = Vector3.one;
            Show(true);
        }
    }
    private static void ApplyRootPose(RectTransform root, Picture p, Picture previous, float progress, Vector3 center, float mapScale)
    {
        root.SetPositionAndRotation(center + Vector3.Lerp(previous.Position, p.Position, progress) * mapScale,
            Quaternion.Slerp(previous.Rotation, p.Rotation, progress));
        root.localScale = Vector3.Lerp(previous.Scale, p.Scale, progress) * mapScale;
    }
    private static void Apply(RectTransform r, Node n, Node previous, float progress)
    {
        float[] a = n.Rect;
        if (previous.Kind != n.Kind || previous.Text != n.Text) { previous = n; progress = 1f; }
        float F(int i) => Mathf.Lerp(previous.Rect[i], a[i], progress);
        r.anchorMin = new(F(0), F(1)); r.anchorMax = new(F(2), F(3)); r.pivot = new(F(4), F(5));
        r.sizeDelta = new(F(6), F(7)); r.anchoredPosition3D = new(F(8), F(9), F(10));
        r.localScale = new(F(11), F(12), F(13)); r.localRotation = Quaternion.Slerp(
            new(previous.Rect[14], previous.Rect[15], previous.Rect[16], previous.Rect[17]), new(a[14], a[15], a[16], a[17]), progress);
        r.gameObject.SetActive(n.Active);
        CanvasGroup? group = r.GetComponent<CanvasGroup>(); if (group != null) group.alpha = Mathf.Lerp(previous.Alpha, n.Alpha, progress);
        Graphic? g = r.GetComponent<Graphic>();
        if (g != null) { g.enabled = n.Enabled; g.color = UnityEngine.Color.Lerp(previous.Color, n.Color, progress);
            g.canvasRenderer.SetColor(UnityEngine.Color.Lerp(previous.RendererColor, n.RendererColor, progress)); g.raycastTarget = false; }
        if (g is TMP_Text t && n.Kind == 2)
        {
            float[] s = n.TextStyle;
            t.text = n.Text; t.fontSize = s[0]; t.fontSizeMin = s[1]; t.fontSizeMax = s[2]; t.characterSpacing = s[3]; t.wordSpacing = s[4];
            t.lineSpacing = s[5]; t.paragraphSpacing = s[6]; t.fontWeight = (FontWeight)s[7]; t.fontStyle = (FontStyles)s[8];
            t.alignment = (TextAlignmentOptions)s[9]; t.overflowMode = (TextOverflowModes)s[10]; t.enableAutoSizing = s[11] != 0;
            t.enableWordWrapping = s[12] != 0; t.richText = s[13] != 0; t.margin = new(s[14], s[15], s[16], s[17]);
        }
    }
    private static float ParentAlpha(Transform? parent)
    {
        float alpha = 1f;
        for (Transform? t = parent; t != null; t = t.parent)
        {
            CanvasGroup? group = t.GetComponent<CanvasGroup>();
            if (group == null || !group.enabled) continue;
            alpha *= group.alpha;
            if (group.ignoreParentGroups) break;
        }
        return alpha;
    }
    private static TMP_SpriteAsset? ResolveSpriteAsset(string name)
    {
        if (name.Length == 0) return null;
        if (SpriteAssets.TryGetValue(name, out TMP_SpriteAsset? asset) && asset != null) return asset;
        foreach (TMP_SpriteAsset candidate in Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>())
            if (candidate != null && candidate.name == name) { SpriteAssets[name] = candidate; return candidate; }
        throw new InvalidOperationException("original tooltip inline-sprite asset unavailable");
    }
    private static void Write(BinaryWriter w, Picture p)
    {
        w.Write(p.Detail); Vector(w, p.Position); Vector(w, p.Scale); w.Write(p.Rotation.x); w.Write(p.Rotation.y); w.Write(p.Rotation.z); w.Write(p.Rotation.w);
        w.Write((byte)p.Lines.Length); foreach (string line in p.Lines) Text(w, line);
        w.Write((byte)p.Nodes.Length);
        foreach (Node n in p.Nodes)
        {
            foreach (float f in n.Rect) w.Write(f); w.Write(n.Active); w.Write(n.Alpha); w.Write(n.Kind); w.Write(n.Enabled);
            Color(w, n.Color); Color(w, n.RendererColor);
            if (n.Kind == 2) { Text(w, n.Text); foreach (float f in n.TextStyle) w.Write(f); }
        }
    }
    internal static bool TryRead(byte[] bytes, out object? unused)
    { bool ok = TryRead(bytes, out Picture[] pictures); unused = ok ? pictures : null; return ok; }
    private static bool TryRead(byte[] bytes, out Picture[] pictures)
    {
        pictures = Array.Empty<Picture>();
        if (bytes.Length < 4 || bytes.Length > MaxPayload) return false;
        try
        {
            using var stream = new MemoryStream(bytes, false); using var r = new BinaryReader(stream);
            if (r.ReadByte() != 1) return false; byte key = r.ReadByte(); if (key == 0) return false;
            int count = r.ReadByte(); if (count == 0 || count > 2) return false; var result = new Picture[count];
            for (int i = 0; i < count; i++)
            {
                var p = new Picture { Key = key, Detail = Bool(r), Position = Vector(r), Scale = Vector(r),
                    Rotation = new Quaternion(Number(r), Number(r), Number(r), Number(r)) };
                if (p.Detail != (i == 1) || p.Scale.sqrMagnitude > 10f
                    || Mathf.Abs(Quaternion.Dot(p.Rotation, p.Rotation) - 1f) > .01f) return false;
                int lines = r.ReadByte(); if (lines > 16 || lines % 4 != 0) return false; p.Lines = new string[lines];
                for (int j = 0; j < lines; j++) { p.Lines[j] = Text(r); if (j % 4 == 0 && p.Lines[j] != "0" && p.Lines[j] != "1" && p.Lines[j] != "2" && p.Lines[j] != "3") return false; }
                int nodes = r.ReadByte(); if (nodes == 0 || nodes > MaxNodes) return false; p.Nodes = new Node[nodes];
                for (int j = 0; j < nodes; j++)
                {
                    Node n = new() { Rect = Numbers(r, 18), Active = Bool(r), Alpha = Number(r), Kind = r.ReadByte(), Enabled = Bool(r), Color = Color(r), RendererColor = Color(r) };
                    if (!NativeDecisionPromptState.RectValid(n.Rect) || n.Alpha < 0 || n.Alpha > 1 || n.Kind > 2) return false;
                    if (n.Kind == 2) { n.Text = Text(r); n.TextStyle = Numbers(r, 18); if (!TextStyleValid(n.TextStyle)) return false; }
                    p.Nodes[j] = n;
                }
                result[i] = p;
            }
            if (stream.Position != stream.Length) return false; pictures = result; return true;
        }
        catch (Exception e) when (e is IOException || e is InvalidDataException || e is ArgumentException || e is OverflowException) { return false; }
    }
    private static bool TextStyleValid(float[] s) => s[0] >= 0 && s[0] <= 1000 && s[1] >= 0 && s[2] >= s[1] && s[2] <= 1000
        && s[7] >= 100 && s[7] <= 900 && s[7] % 100 == 0 && s[8] >= 0 && s[8] <= 4095 && s[8] == (int)s[8]
        && s[9] >= 0 && s[9] <= 65535 && s[9] == (int)s[9] && s[10] >= 0 && s[10] <= 7 && s[10] == (int)s[10]
        && (s[11] == 0 || s[11] == 1) && (s[12] == 0 || s[12] == 1) && (s[13] == 0 || s[13] == 1);
    private static float Number(BinaryReader r) { float f = r.ReadSingle(); if (float.IsNaN(f) || float.IsInfinity(f) || Mathf.Abs(f) > 100000f) throw new InvalidDataException(); return f; }
    private static float[] Numbers(BinaryReader r, int count) { var a = new float[count]; for (int i = 0; i < count; i++) a[i] = Number(r); return a; }
    private static bool Bool(BinaryReader r) { byte b = r.ReadByte(); if (b > 1) throw new InvalidDataException(); return b != 0; }
    private static void Vector(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    private static Vector3 Vector(BinaryReader r) => new(Number(r), Number(r), Number(r));
    private static void Color(BinaryWriter w, Color c) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
    private static Color Color(BinaryReader r) => new(Number(r), Number(r), Number(r), Number(r));
    private static void Text(BinaryWriter w, string text) { byte[] b = NativeDecisionPromptState.Utf8.GetBytes(text); if (b.Length > 4096) throw new InvalidDataException(); w.Write((ushort)b.Length); w.Write(b); }
    private static string Text(BinaryReader r) { int n = r.ReadUInt16(); if (n > 4096) throw new InvalidDataException(); byte[] b = r.ReadBytes(n); if (b.Length != n) throw new EndOfStreamException(); return NativeDecisionPromptState.Utf8.GetString(b); }
    private static bool Same(byte[] a, byte[] b, int length) { if (a.Length != length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    private static void Report(Exception e) { if (_refused) return; _refused = true; VRLog.Warn("MapRoom", "MAP BUTTON TOOLTIP: original presentation unavailable: " + e.Message); }
}
