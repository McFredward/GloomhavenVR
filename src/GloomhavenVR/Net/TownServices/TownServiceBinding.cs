using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Reads outputs of original widgets and applies them to inert copies of those widgets.</summary>
internal sealed class TownServiceBinding : IDisposable
{
    internal readonly Transform Root;
    internal readonly Transform[] Nodes;
    internal readonly uint[] Bindings;
    internal readonly uint Structure;
    private readonly Material?[] _graphicMaterials, _textMaterials;
    private TownServiceNode[]? _lastApplied;
    private readonly List<TownServiceFlameClock> _flameClocks = new();
    private readonly Func<Transform, bool>? _exclude;
    private readonly NodeCache[] _cache;
    private readonly TownServiceNode[] _sampled;
    private sealed class NodeCache
    {
        internal Transform Transform = null!;
        internal Transform? Parent;
        internal RectTransform? Rect;
        internal Canvas? Canvas;
        internal MeshRenderer? Mesh;
        internal readonly List<Material> MeshMaterials = new();
        internal readonly List<TownServiceValue> MeshValues = new();
        internal readonly Material?[] OwnedMesh = new Material?[8];
        internal Material[]? AppliedMesh;
        internal readonly TownServiceFlameClock?[] FlameClocks = new TownServiceFlameClock?[8];
        internal int Sibling;
        internal Component[] Components = Array.Empty<Component>();
        internal readonly List<Component> ComponentProbe = new();
        internal Graphic? Graphic;
        internal CanvasGroup? Group;
        internal Mask? Mask;
        internal RectMask2D? RectMask;
        internal bool Dirty = true;
        internal NodeProbe Probe;
        internal int Children;
        internal TownServiceValue? Material;
        internal UnityEngine.Events.UnityAction Callback = null!;
        internal void MarkDirty() => Dirty = true;
    }
    private struct NodeProbe
    {
        internal Vector3 Position, Scale, Anchored;
        internal Quaternion Rotation;
        internal Vector2 AnchorMin, AnchorMax, Pivot, Size;
        internal Color Color, Rendered;
        internal Vector4 Padding;
        internal Vector2Int Softness;
        internal int Flags, Sibling, SortingOrder, SortingLayer, ShaderChannels;
        internal bool CanvasEnabled, OverrideSorting, PixelPerfect, OverridePixelPerfect;
        internal float ReferencePixels, PlaneDistance;
        internal float Alpha;
        internal bool Same(NodeProbe p) => Position.Equals(p.Position) && Scale.Equals(p.Scale) && Anchored.Equals(p.Anchored)
            && Rotation.Equals(p.Rotation) && AnchorMin.Equals(p.AnchorMin) && AnchorMax.Equals(p.AnchorMax)
            && Pivot.Equals(p.Pivot) && Size.Equals(p.Size) && Color.Equals(p.Color) && Rendered.Equals(p.Rendered)
            && Padding.Equals(p.Padding) && Softness.Equals(p.Softness) && Flags == p.Flags && Alpha == p.Alpha && Sibling == p.Sibling
            && SortingOrder == p.SortingOrder && SortingLayer == p.SortingLayer && ShaderChannels == p.ShaderChannels
            && CanvasEnabled == p.CanvasEnabled && OverrideSorting == p.OverrideSorting && PixelPerfect == p.PixelPerfect
            && OverridePixelPerfect == p.OverridePixelPerfect && ReferencePixels == p.ReferencePixels && PlaneDistance == p.PlaneDistance;
    }

    internal TownServiceBinding(Transform root, Func<Transform, bool>? exclude = null)
    {
        Root = root; _exclude = exclude;
        var nodes = new List<Transform>(); var keys = new List<uint>();
        Walk(root, 2166136261, nodes, keys, true, 0, exclude);
        SortBindings(nodes, keys);
        Nodes = nodes.ToArray(); Bindings = keys.ToArray();
        uint signature = 2166136261;
        foreach (uint key in Bindings) signature = unchecked((signature ^ key) * 16777619);
        Structure = signature == 0 ? 1 : signature;
        _graphicMaterials = new Material?[Nodes.Length]; _textMaterials = new Material?[Nodes.Length];
        _cache = new NodeCache[Nodes.Length]; _sampled = new TownServiceNode[Nodes.Length];
        for (int i = 0; i < Nodes.Length; i++)
        {
            Transform node = Nodes[i];
            var cache = new NodeCache { Transform = node, Parent = node.parent, Children = node.childCount,
                Rect = node as RectTransform, Canvas = node.GetComponent<Canvas>(), Mesh = node.GetComponent<MeshRenderer>(), Sibling = node.GetSiblingIndex(), Graphic = node.GetComponent<Graphic>(), Group = node.GetComponent<CanvasGroup>(),
                Mask = node.GetComponent<Mask>(), RectMask = node.GetComponent<RectMask2D>() };
            cache.Components = node.GetComponents<Component>();
            cache.Callback = cache.MarkDirty;
            if (cache.Graphic != null)
            {
                cache.Graphic.RegisterDirtyVerticesCallback(cache.Callback);
                cache.Graphic.RegisterDirtyMaterialCallback(cache.Callback);
                cache.Graphic.RegisterDirtyLayoutCallback(cache.Callback);
            }
            _cache[i] = cache;
        }
    }

    private static void Walk(Transform node, uint parent, List<Transform> nodes, List<uint> keys, bool root,
        int sameNameIndex, Func<Transform, bool>? exclude)
    {
        if (nodes.Count >= TownServiceFrame.MaxNodes)
            throw new InvalidDataException("Town-service module exceeds256 nodes; register dynamic rows as separate modules.");
        uint key = parent;
        // Root names may gain Unity's Clone suffix; descendants retain their original names.
        string identity = root ? "root" : node.name + ":" + sameNameIndex;
        foreach (char c in identity) key = unchecked((key ^ c) * 16777619);
        if (key == 0) key = 1;
        nodes.Add(node); keys.Add(key);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < node.childCount; i++)
        {
            Transform child = node.GetChild(i);
            if (GeneratedTextMesh(child) || (exclude != null && exclude(child))) continue;
            counts.TryGetValue(child.name, out int occurrence); counts[child.name] = occurrence + 1;
            Walk(child, key, nodes, keys, false, occurrence, exclude);
        }
    }

    private static void SortBindings(List<Transform> nodes, List<uint> keys)
    {
        // Keep the module root first, then order by stable original path identity. Sibling
        // order is an authored property, not topology: reordering peers must not replace art.
        var order = new List<int>(); for (int i = 1; i < keys.Count; i++) order.Add(i);
        order.Sort((a, b) => keys[a].CompareTo(keys[b]));
        Transform[] originalNodes = nodes.ToArray(); uint[] originalKeys = keys.ToArray();
        for (int i = 0; i < order.Count; i++) { nodes[i + 1] = originalNodes[order[i]]; keys[i + 1] = originalKeys[order[i]]; }
    }

    internal static bool GeneratedTextMesh(Transform child) => child.GetComponent<TMP_SubMeshUI>() != null || child.GetComponent<TMP_SubMesh>() != null;

    internal TownServiceNode[] Read(TownServiceAssets assets)
    {
        TownServiceNode[] result = _sampled;
        bool checkStructure = false;
        for (int i = 0; i < Nodes.Length; i++)
        {
            Transform node = Nodes[i];
            if (node == null) throw new InvalidDataException("Native town-service module was destroyed.");
            NodeCache cache = _cache[i];
            if (node.childCount != cache.Children || i != 0 && node.parent != cache.Parent || node.GetSiblingIndex() != cache.Sibling) checkStructure = true;
            node.GetComponents(cache.ComponentProbe);
            if (cache.ComponentProbe.Count != cache.Components.Length) throw new InvalidDataException("Native town-service topology changed.");
            for (int c = 0; c < cache.Components.Length; c++)
                if (!ReferenceEquals(cache.Components[c], cache.ComponentProbe[c])) throw new InvalidDataException("Native town-service topology changed.");
            bool meshChanged = ReadMesh(cache, assets);
            bool draw = cache.Graphic != null && cache.Graphic.enabled && node.gameObject.activeInHierarchy;
            TownServiceValue? material = draw
                ? TownServiceMaterial.Read(cache.Graphic is TMP_Text tm ? tm.fontSharedMaterial : cache.Graphic!.material, assets) : null;
            NodeProbe probe = Probe(cache, i == 0);
            if (!cache.Dirty && probe.Same(cache.Probe) && ReferenceEquals(material, cache.Material) && !meshChanged) continue;
            var value = new TownServiceNode { Binding = Bindings[i] };
            var v = value.Values;
            RectTransform? rect = cache.Rect;
            Vector3 p = rect != null ? rect.anchoredPosition3D : node.localPosition;
            Vector3 s = node.localScale; Quaternion q = node.localRotation;
            Put(v, TownServiceProperty.Transform, rect == null
                ? new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z }
                : new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z,
                    rect.anchorMin.x, rect.anchorMin.y, rect.anchorMax.x, rect.anchorMax.y,
                    rect.pivot.x, rect.pivot.y, i == 0 ? rect.rect.width : rect.sizeDelta.x, i == 0 ? rect.rect.height : rect.sizeDelta.y });
            Put(v, TownServiceProperty.Active, new[] { node.gameObject.activeSelf ? 1f : 0f });
            Put(v, TownServiceProperty.Sibling, new[] { (float)node.GetSiblingIndex() });
            if (cache.Canvas != null)
                Put(v, TownServiceProperty.Canvas, new[] { probe.CanvasEnabled ? 1f : 0f, probe.OverrideSorting ? 1f : 0f,
                    probe.SortingOrder, probe.PixelPerfect ? 1f : 0f, probe.OverridePixelPerfect ? 1f : 0f,
                    probe.ReferencePixels, probe.PlaneDistance, probe.ShaderChannels }, probe.SortingLayer.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (cache.Mesh != null)
            {
                Put(v, TownServiceProperty.Mesh, new[] { cache.Mesh.enabled ? 1f : 0f, cache.Mesh.forceRenderingOff ? 1f : 0f,
                    (float)cache.MeshMaterials.Count, cache.Mesh.sortingOrder, (float)cache.Mesh.shadowCastingMode, cache.Mesh.receiveShadows ? 1f : 0f },
                    cache.Mesh.sortingLayerID.ToString(System.Globalization.CultureInfo.InvariantCulture));
                for (int m = 0; m < cache.MeshValues.Count; m++) v.Add((ushort)(TownServiceProperty.MeshMaterial0 + m), cache.MeshValues[m]);
            }
            Graphic? graphic = cache.Graphic;
            if (graphic != null)
            {
                Color c = graphic.color;
                Put(v, TownServiceProperty.Graphic, new[] { graphic.enabled ? 1f : 0f, c.r, c.g, c.b, c.a });
                Color rendered = graphic.canvasRenderer.GetColor();
                Put(v, TownServiceProperty.Renderer, new[] { rendered.r, rendered.g, rendered.b, rendered.a });
                if (draw && graphic is not TMP_Text) v.Add(TownServiceProperty.Material, material!);
            }
            CanvasGroup? group = cache.Group;
            if (group != null) Put(v, TownServiceProperty.Group, new[] { group.enabled ? 1f : 0f, group.alpha, group.ignoreParentGroups ? 1f : 0f });
            if (draw && graphic is Image image)
                Put(v, TownServiceProperty.Image, new[] { (float)image.type, image.fillAmount, (float)image.fillMethod,
                    (float)image.fillOrigin, image.fillClockwise ? 1f : 0f, image.preserveAspect ? 1f : 0f,
                    image.fillCenter ? 1f : 0f, image.pixelsPerUnitMultiplier }, assets.Key(image.sprite), assets.Key(image.overrideSprite));
            else if (draw && graphic is RawImage raw)
            { Rect uv = raw.uvRect; Put(v, TownServiceProperty.RawImage, new[] { uv.x, uv.y, uv.width, uv.height }, assets.Key(raw.texture)); }
            else if (draw && graphic is TMP_Text text)
            {
                Vector4 margin = text.margin; VertexGradient gradient = text.colorGradient;
                var n = new List<float> { text.fontSize, (float)text.fontStyle, (float)text.alignment,
                    text.enableWordWrapping ? 1f : 0f, text.richText ? 1f : 0f, text.isRightToLeftText ? 1f : 0f,
                    (float)text.overflowMode, text.enableAutoSizing ? 1f : 0f, text.fontSizeMin, text.fontSizeMax,
                    text.characterSpacing, text.wordSpacing, text.lineSpacing, text.paragraphSpacing,
                    margin.x, margin.y, margin.z, margin.w, text.maxVisibleCharacters, text.maxVisibleWords,
                    text.maxVisibleLines, text.firstVisibleCharacter, text.pageToDisplay, text.enableVertexGradient ? 1f : 0f };
                Color[] colors = { gradient.topLeft, gradient.topRight, gradient.bottomLeft, gradient.bottomRight };
                foreach (Color c in colors) { n.Add(c.r); n.Add(c.g); n.Add(c.b); n.Add(c.a); }
                Put(v, TownServiceProperty.TmpText, n.ToArray(), text.text ?? string.Empty, assets.Key(text.font), assets.Key(text.spriteAsset));
                v.Add(TownServiceProperty.TextMaterial, material!);
            }
            else if (draw && graphic is Text legacy)
                Put(v, TownServiceProperty.LegacyText, new[] { (float)legacy.fontSize, (float)legacy.fontStyle,
                    (float)legacy.alignment, legacy.supportRichText ? 1f : 0f, (float)legacy.horizontalOverflow,
                    (float)legacy.verticalOverflow, legacy.lineSpacing, legacy.resizeTextForBestFit ? 1f : 0f,
                    legacy.resizeTextMinSize, legacy.resizeTextMaxSize, legacy.alignByGeometry ? 1f : 0f }, legacy.text ?? string.Empty, assets.Key(legacy.font));
            Mask? mask = node.GetComponent<Mask>();
            if (mask != null) Put(v, TownServiceProperty.Mask, new[] { mask.enabled ? 1f : 0f, mask.showMaskGraphic ? 1f : 0f });
            RectMask2D? clip = node.GetComponent<RectMask2D>();
            if (clip != null) { Vector4 pad = clip.padding; Vector2Int soft = clip.softness;
                Put(v, TownServiceProperty.RectMask, new[] { clip.enabled ? 1f : 0f, pad.x, pad.y, pad.z, pad.w, (float)soft.x, soft.y }); }
            foreach (Shadow shadow in node.GetComponents<Shadow>())
            {
                ushort key = shadow is Outline ? TownServiceProperty.Outline : TownServiceProperty.Shadow;
                Color c = shadow.effectColor; Vector2 distance = shadow.effectDistance;
                if (v.ContainsKey(key)) throw new InvalidDataException("Multiple same-kind native mesh effects need distinct bindings.");
                Put(v, key, new[] { shadow.enabled ? 1f : 0f, c.r, c.g, c.b, c.a, distance.x, distance.y, shadow.useGraphicAlpha ? 1f : 0f });
            }
            result[i] = value;
            cache.Dirty = false; cache.Probe = probe; cache.Material = material;
        }
        // A regenerated row must be rebound as a complete new module, not partially sampled.
        if (checkStructure)
        {
            var verify = new List<Transform>(); var keys = new List<uint>(); Walk(Root, 2166136261, verify, keys, true, 0, _exclude);
            SortBindings(verify, keys);
            if (verify.Count != Nodes.Length) throw new InvalidDataException("Native town-service topology changed.");
            for (int i = 0; i < verify.Count; i++)
            {
                if (!ReferenceEquals(verify[i], Nodes[i]) || keys[i] != Bindings[i]) throw new InvalidDataException("Native town-service topology changed.");
                _cache[i].Children = Nodes[i].childCount; _cache[i].Parent = Nodes[i].parent; _cache[i].Sibling = Nodes[i].GetSiblingIndex();
            }
        }
        return result;
    }

    private static bool ReadMesh(NodeCache cache, TownServiceAssets assets)
    {
        if (cache.Mesh == null) return false;
        if (cache.Mesh.HasPropertyBlock()) throw new InvalidDataException("Town mesh property block requires declared shader output capture.");
        cache.Mesh.GetSharedMaterials(cache.MeshMaterials);
        if (cache.MeshMaterials.Count > 8) throw new InvalidDataException("Town mesh exceeds eight original material slots.");
        bool changed = cache.MeshValues.Count != cache.MeshMaterials.Count;
        while (cache.MeshValues.Count < cache.MeshMaterials.Count) cache.MeshValues.Add(new TownServiceValue());
        if (cache.MeshValues.Count > cache.MeshMaterials.Count) cache.MeshValues.RemoveRange(cache.MeshMaterials.Count, cache.MeshValues.Count - cache.MeshMaterials.Count);
        for (int i = 0; i < cache.MeshMaterials.Count; i++)
        {
            TownServiceValue value = TownServiceMaterial.Read(cache.MeshMaterials[i], assets);
            if (!ReferenceEquals(value, cache.MeshValues[i])) { cache.MeshValues[i] = value; changed = true; }
        }
        return changed;
    }

    private static NodeProbe Probe(NodeCache c, bool root)
    {
        Transform t = c.Transform;
        var p = new NodeProbe { Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale,
            Flags = (t.gameObject.activeSelf ? 1 : 0) | (t.gameObject.activeInHierarchy ? 512 : 0), Sibling = t.GetSiblingIndex() };
        if (c.Canvas != null)
        { Canvas canvas = c.Canvas; p.CanvasEnabled = canvas.enabled; p.OverrideSorting = canvas.overrideSorting;
            p.SortingOrder = canvas.sortingOrder; p.SortingLayer = canvas.sortingLayerID; p.PixelPerfect = canvas.pixelPerfect;
            p.OverridePixelPerfect = canvas.overridePixelPerfect; p.ReferencePixels = canvas.referencePixelsPerUnit;
            p.PlaneDistance = canvas.planeDistance; p.ShaderChannels = (int)canvas.additionalShaderChannels; }
        if (c.Mesh != null) { if (c.Mesh.enabled) p.Flags |= 128; if (c.Mesh.forceRenderingOff) p.Flags |= 256; }
        RectTransform? r = c.Rect;
        if (r != null)
        {
            p.Anchored = r.anchoredPosition3D; p.AnchorMin = r.anchorMin; p.AnchorMax = r.anchorMax;
            p.Pivot = r.pivot; p.Size = root ? r.rect.size : r.sizeDelta;
        }
        Graphic? g = c.Graphic;
        if (g != null) { if (g.enabled) p.Flags |= 2; p.Color = g.color; p.Rendered = g.canvasRenderer.GetColor(); }
        CanvasGroup? group = c.Group;
        if (group != null) { if (group.enabled) p.Flags |= 4; p.Alpha = group.alpha; if (group.ignoreParentGroups) p.Flags |= 8; }
        if (c.Mask != null) { if (c.Mask.enabled) p.Flags |= 16; if (c.Mask.showMaskGraphic) p.Flags |= 32; }
        if (c.RectMask != null) { if (c.RectMask.enabled) p.Flags |= 64; p.Padding = c.RectMask.padding; p.Softness = c.RectMask.softness; }
        return p;
    }

    private static void Put(Dictionary<ushort, TownServiceValue> values, ushort key, float[] numbers, params string[] text)
        => values.Add(key, new TownServiceValue { Numbers = numbers, Text = text });

    internal void Validate(TownServiceFrame frame, TownServiceAssets assets)
    {
        if (frame.Structure != Structure || frame.Nodes.Length != Nodes.Length)
            throw new InvalidDataException("Original town-service template differs between peers.");
        for (int i = 0; i < Nodes.Length; i++)
        {
            TownServiceNode state = frame.Nodes[i]; Transform node = Nodes[i];
            if (state.Binding != Bindings[i]) throw new InvalidDataException("Original town-service node binding differs.");
            foreach (var pair in state.Values)
            {
                TownServiceValue value = pair.Value;
                switch (pair.Key)
                {
                    case TownServiceProperty.Transform: Shape(value, node is RectTransform ? 18 : 10, 0); break;
                    case TownServiceProperty.Active: Shape(value, 1, 0); break;
                    case TownServiceProperty.Sibling: Shape(value, 1, 0); break;
                    case TownServiceProperty.Canvas: Shape(value, 8, 1); int.Parse(value.Text[0], System.Globalization.CultureInfo.InvariantCulture); break;
                    case TownServiceProperty.Mesh: Require<MeshRenderer>(node); Shape(value, 6, 1); int.Parse(value.Text[0], System.Globalization.CultureInfo.InvariantCulture); break;
                    case TownServiceProperty.Graphic: Require<Graphic>(node); Shape(value, 5, 0); break;
                    case TownServiceProperty.Renderer: Require<CanvasRenderer>(node); Shape(value, 4, 0); break;
                    case TownServiceProperty.Group: Shape(value, 3, 0); break;
                    case TownServiceProperty.Image:
                        Require<Image>(node); Shape(value, 8, 2); assets.Resolve<Sprite>(value.Text[0]); assets.Resolve<Sprite>(value.Text[1]); break;
                    case TownServiceProperty.RawImage: Require<RawImage>(node); Shape(value, 4, 1); assets.Resolve<Texture>(value.Text[0]); break;
                    case TownServiceProperty.TmpText: Require<TMP_Text>(node); Shape(value, 40, 3); assets.Resolve<TMP_FontAsset>(value.Text[1]); assets.Resolve<TMP_SpriteAsset>(value.Text[2]); break;
                    case TownServiceProperty.LegacyText: Require<Text>(node); Shape(value, 11, 2); assets.Resolve<Font>(value.Text[1]); break;
                    case TownServiceProperty.Mask: Shape(value, 2, 0); break;
                    case TownServiceProperty.RectMask: Shape(value, 7, 0); break;
                    case TownServiceProperty.Material: Require<Graphic>(node); TownServiceMaterial.Validate(value, assets); break;
                    case TownServiceProperty.TextMaterial: Require<TMP_Text>(node); TownServiceMaterial.Validate(value, assets); break;
                    case TownServiceProperty.Shadow: Shape(value, 8, 0); break;
                    case TownServiceProperty.Outline: Shape(value, 8, 0); break;
                    default:
                        if (pair.Key >= TownServiceProperty.MeshMaterial0 && pair.Key <= TownServiceProperty.MeshMaterial7)
                        { Require<MeshRenderer>(node); TownServiceMaterial.Validate(value, assets); break; }
                        throw new InvalidDataException("Unsupported town-service property.");
                }
            }
            if (!state.Values.ContainsKey(TownServiceProperty.Transform) || !state.Values.ContainsKey(TownServiceProperty.Active))
                throw new InvalidDataException("Incomplete town-service node.");
        }
    }
    private static T Require<T>(Transform node) where T : Component
    {
        T? component = node.GetComponent<T>(); if (component != null) return component;
        Type type = typeof(T);
        if (type == typeof(CanvasGroup) || type == typeof(Canvas) || type == typeof(Mask) || type == typeof(RectMask2D)
            || type == typeof(Shadow) || type == typeof(Outline)) return node.gameObject.AddComponent<T>();
        throw new InvalidDataException("Original town-service component is absent: " + type.Name);
    }
    private static void Shape(TownServiceValue value, int numbers, int text)
    { if (value.Numbers.Length != numbers || value.Text.Length != text) throw new InvalidDataException("Malformed town-service property."); }

    internal void Apply(TownServiceFrame frame, TownServiceAssets assets)
    {
        for (int i = 0; i < Nodes.Length; i++)
        {
            Transform node = Nodes[i]; TownServiceNode state = frame.Nodes[i];
            DisableAbsent<CanvasGroup>(node, state, TownServiceProperty.Group);
            DisableAbsent<Mask>(node, state, TownServiceProperty.Mask);
            DisableAbsent<RectMask2D>(node, state, TownServiceProperty.RectMask);
            DisableAbsent<Shadow>(node, state, TownServiceProperty.Shadow);
            DisableAbsent<Outline>(node, state, TownServiceProperty.Outline);
            if (!state.Values.ContainsKey(TownServiceProperty.Canvas))
            { Canvas? oldCanvas = node.GetComponent<Canvas>(); if (oldCanvas != null) UnityEngine.Object.DestroyImmediate(oldCanvas); }
            foreach (var pair in state.Values)
            {
                if (_lastApplied != null && _lastApplied[i].Values.TryGetValue(pair.Key, out TownServiceValue? old) && pair.Value.Same(old)) continue;
                TownServiceValue value = pair.Value; float[] n = value.Numbers; string[] text = value.Text;
                switch (pair.Key)
                {
                    case TownServiceProperty.Transform:
                        if (i == 0) break; // module root is positioned through the authored world-frame pose
                        if (node is RectTransform rect)
                        {
                            rect.anchorMin = new Vector2(n[10], n[11]); rect.anchorMax = new Vector2(n[12], n[13]);
                            rect.pivot = new Vector2(n[14], n[15]); rect.sizeDelta = new Vector2(n[16], n[17]);
                            rect.anchoredPosition3D = new Vector3(n[0], n[1], n[2]);
                        }
                        else node.localPosition = new Vector3(n[0], n[1], n[2]);
                        node.localRotation = new Quaternion(n[3], n[4], n[5], n[6]); node.localScale = new Vector3(n[7], n[8], n[9]); break;
                    case TownServiceProperty.Active: if (i != 0) node.gameObject.SetActive(n[0] != 0); break;
                    case TownServiceProperty.Sibling: if (i != 0) node.SetSiblingIndex((int)n[0]); break;
                    case TownServiceProperty.Canvas:
                        Canvas canvas = Require<Canvas>(node); canvas.enabled = n[0] != 0; canvas.overrideSorting = n[1] != 0;
                        canvas.sortingOrder = (int)n[2]; canvas.sortingLayerID = int.Parse(text[0], System.Globalization.CultureInfo.InvariantCulture);
                        canvas.pixelPerfect = n[3] != 0; canvas.overridePixelPerfect = n[4] != 0; canvas.referencePixelsPerUnit = n[5];
                        canvas.planeDistance = n[6]; canvas.additionalShaderChannels = (AdditionalCanvasShaderChannels)(int)n[7]; break;
                    case TownServiceProperty.Mesh:
                        MeshRenderer mesh = Require<MeshRenderer>(node); mesh.enabled = n[0] != 0; mesh.forceRenderingOff = n[1] != 0;
                        mesh.sortingOrder = (int)n[3]; mesh.sortingLayerID = int.Parse(text[0], System.Globalization.CultureInfo.InvariantCulture);
                        mesh.shadowCastingMode = (UnityEngine.Rendering.ShadowCastingMode)(int)n[4]; mesh.receiveShadows = n[5] != 0; break;
                    case TownServiceProperty.Graphic:
                        Graphic g = Require<Graphic>(node); g.enabled = n[0] != 0; g.color = ColorAt(n, 1); g.raycastTarget = false; break;
                    case TownServiceProperty.Renderer: Require<CanvasRenderer>(node).SetColor(ColorAt(n, 0)); break;
                    case TownServiceProperty.Group:
                        CanvasGroup cg = Require<CanvasGroup>(node); cg.enabled = n[0] != 0; cg.alpha = n[1]; cg.ignoreParentGroups = n[2] != 0;
                        cg.interactable = false; cg.blocksRaycasts = false; break;
                    case TownServiceProperty.Image:
                        Image image = Require<Image>(node); image.sprite = assets.Resolve<Sprite>(text[0]); image.overrideSprite = assets.Resolve<Sprite>(text[1]);
                        image.type = (Image.Type)n[0]; image.fillAmount = n[1]; image.fillMethod = (Image.FillMethod)n[2]; image.fillOrigin = (int)n[3];
                        image.fillClockwise = n[4] != 0; image.preserveAspect = n[5] != 0; image.fillCenter = n[6] != 0; image.pixelsPerUnitMultiplier = n[7]; break;
                    case TownServiceProperty.RawImage:
                        RawImage raw = Require<RawImage>(node); raw.texture = assets.Resolve<Texture>(text[0]); raw.uvRect = new Rect(n[0], n[1], n[2], n[3]); break;
                    case TownServiceProperty.TmpText:
                        TMP_Text tmp = Require<TMP_Text>(node); tmp.font = assets.Resolve<TMP_FontAsset>(text[1]); tmp.spriteAsset = assets.Resolve<TMP_SpriteAsset>(text[2]); tmp.text = text[0];
                        tmp.fontSize = n[0]; tmp.fontStyle = (FontStyles)n[1]; tmp.alignment = (TextAlignmentOptions)n[2];
                        tmp.enableWordWrapping = n[3] != 0; tmp.richText = n[4] != 0; tmp.isRightToLeftText = n[5] != 0;
                        tmp.overflowMode = (TextOverflowModes)n[6]; tmp.enableAutoSizing = n[7] != 0; tmp.fontSizeMin = n[8]; tmp.fontSizeMax = n[9];
                        tmp.characterSpacing = n[10]; tmp.wordSpacing = n[11]; tmp.lineSpacing = n[12]; tmp.paragraphSpacing = n[13];
                        tmp.margin = new Vector4(n[14], n[15], n[16], n[17]); tmp.maxVisibleCharacters = SafeInt(n[18]); tmp.maxVisibleWords = SafeInt(n[19]);
                        tmp.maxVisibleLines = SafeInt(n[20]); tmp.firstVisibleCharacter = SafeInt(n[21]); tmp.pageToDisplay = SafeInt(n[22]);
                        tmp.enableVertexGradient = n[23] != 0; tmp.colorGradient = new VertexGradient(ColorAt(n, 24), ColorAt(n, 28), ColorAt(n, 32), ColorAt(n, 36)); break;
                    case TownServiceProperty.LegacyText:
                        Text legacy = Require<Text>(node); legacy.font = assets.Resolve<Font>(text[1]); legacy.text = text[0]; legacy.fontSize = (int)n[0];
                        legacy.fontStyle = (FontStyle)n[1]; legacy.alignment = (TextAnchor)n[2]; legacy.supportRichText = n[3] != 0;
                        legacy.horizontalOverflow = (HorizontalWrapMode)n[4]; legacy.verticalOverflow = (VerticalWrapMode)n[5]; legacy.lineSpacing = n[6];
                        legacy.resizeTextForBestFit = n[7] != 0; legacy.resizeTextMinSize = (int)n[8]; legacy.resizeTextMaxSize = (int)n[9]; legacy.alignByGeometry = n[10] != 0; break;
                    case TownServiceProperty.Mask: Mask mask = Require<Mask>(node); mask.enabled = n[0] != 0; mask.showMaskGraphic = n[1] != 0; break;
                    case TownServiceProperty.RectMask:
                        RectMask2D clip = Require<RectMask2D>(node); clip.enabled = n[0] != 0; clip.padding = new Vector4(n[1], n[2], n[3], n[4]); clip.softness = new Vector2Int((int)n[5], (int)n[6]); break;
                    case TownServiceProperty.Material:
                        _graphicMaterials[i] = TownServiceMaterial.Apply(value, assets, _graphicMaterials[i]); Require<Graphic>(node).material = _graphicMaterials[i]; break;
                    case TownServiceProperty.TextMaterial:
                        _textMaterials[i] = TownServiceMaterial.Apply(value, assets, _textMaterials[i]); Require<TMP_Text>(node).fontSharedMaterial = _textMaterials[i]; break;
                    default:
                        if (pair.Key >= TownServiceProperty.MeshMaterial0 && pair.Key <= TownServiceProperty.MeshMaterial7)
                        {
                            int slot = pair.Key - TownServiceProperty.MeshMaterial0;
                            NodeCache cache = _cache[i];
                            cache.OwnedMesh[slot] = TownServiceMaterial.ApplyMesh(value, assets, cache.OwnedMesh[slot], out bool animated, out float clock);
                            if (animated)
                            {
                                if (cache.FlameClocks[slot] == null)
                                {
                                    cache.FlameClocks[slot] = new TownServiceFlameClock(Require<MeshRenderer>(node), slot);
                                    _flameClocks.Add(cache.FlameClocks[slot]!);
                                }
                                cache.FlameClocks[slot]!.Sample(frame.Session, frame.SampleTime, clock, Time.unscaledTime);
                            }
                            else ClearClock(cache, slot);
                        }
                        break;
                    case TownServiceProperty.Shadow:
                    case TownServiceProperty.Outline:
                        Shadow effect = pair.Key == TownServiceProperty.Outline ? Require<Outline>(node) : Require<Shadow>(node);
                        effect.enabled = n[0] != 0; effect.effectColor = ColorAt(n, 1); effect.effectDistance = new Vector2(n[5], n[6]); effect.useGraphicAlpha = n[7] != 0; break;
                }
            }
        }
        for (int i = 0; i < Nodes.Length; i++)
            if (frame.Nodes[i].Values.TryGetValue(TownServiceProperty.Mesh, out TownServiceValue? meshState))
            {
                int count = (int)meshState.Numbers[2];
                for (int m = count; m < _cache[i].OwnedMesh.Length; m++)
                {
                    ClearClock(_cache[i], m);
                    TownServiceMaterial.Release(_cache[i].OwnedMesh[m]); _cache[i].OwnedMesh[m] = null;
                }
                NodeCache cache = _cache[i];
                bool changed = cache.AppliedMesh == null || cache.AppliedMesh.Length != count;
                if (changed) cache.AppliedMesh = new Material[count];
                for (int m = 0; m < count; m++)
                {
                    changed |= cache.AppliedMesh![m] != cache.OwnedMesh[m];
                    cache.AppliedMesh![m] = cache.OwnedMesh[m]!;
                }
                if (changed) Require<MeshRenderer>(Nodes[i]).sharedMaterials = cache.AppliedMesh;
            }
        // Root rect dimensions still govern its original children's anchors and wrapping.
        float[] root = frame.Nodes[0].Values[TownServiceProperty.Transform].Numbers;
        if (Root is RectTransform rr && root.Length == 18)
        { rr.anchorMin = rr.anchorMax = new Vector2(.5f, .5f); rr.pivot = new Vector2(root[14], root[15]); rr.sizeDelta = new Vector2(root[16], root[17]); }
        _lastApplied = frame.Nodes;
    }
    private static void DisableAbsent<T>(Transform node, TownServiceNode state, ushort key) where T : Behaviour
    {
        if (state.Values.ContainsKey(key)) return;
        T? component = node.GetComponent<T>();
        if (component != null && component.GetType() == typeof(T)) component.enabled = false;
    }
    private static int SafeInt(float value) => value >= int.MaxValue ? int.MaxValue : (int)value;
    private static Color ColorAt(float[] n, int i) => new(n[i], n[i + 1], n[i + 2], n[i + 3]);
    private void ClearClock(NodeCache cache, int slot)
    {
        TownServiceFlameClock? clock = cache.FlameClocks[slot];
        if (clock == null) return;
        clock.Dispose(); _flameClocks.Remove(clock); cache.FlameClocks[slot] = null;
    }
    internal void TickAnimation(float now)
    {
        if (Root == null || !Root.gameObject.activeInHierarchy) return;
        foreach (TownServiceFlameClock clock in _flameClocks) clock.Tick(now);
    }
    public void Dispose()
    {
        foreach (TownServiceFlameClock clock in _flameClocks) clock.Dispose();
        _flameClocks.Clear();
        foreach (NodeCache node in _cache)
            if (node.Graphic != null)
            {
                node.Graphic.UnregisterDirtyVerticesCallback(node.Callback);
                node.Graphic.UnregisterDirtyMaterialCallback(node.Callback);
                node.Graphic.UnregisterDirtyLayoutCallback(node.Callback);
            }
        foreach (NodeCache node in _cache) foreach (Material? material in node.OwnedMesh) TownServiceMaterial.Release(material);
        foreach (Material? material in _graphicMaterials) TownServiceMaterial.Release(material);
        foreach (Material? material in _textMaterials) TownServiceMaterial.Release(material);
        Array.Clear(_graphicMaterials, 0, _graphicMaterials.Length); Array.Clear(_textMaterials, 0, _textMaterials.Length);
        _lastApplied = null;
    }
}
