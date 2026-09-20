using System;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Interpolate authored native output between packets, including its clipping canvas.
/// No native animator, layout controller or gameplay callback runs on these observer widgets.</summary>
internal sealed class TownServiceMotion
{
    private sealed class Node
    {
        internal Transform Transform = null!;
        internal RectTransform? Rect;
        internal CanvasGroup? Group;
        internal Graphic? Graphic;
    }
    private struct State
    {
        internal Transform? Parent;
        internal bool Active;
        internal Vector3 Position, Scale;
        internal Quaternion Rotation;
        internal Vector2 Min, Max, Pivot, Size;
        internal float Alpha;
        internal Color Color, Rendered;
        internal bool Same(State other) => Parent == other.Parent && Active == other.Active && Position == other.Position
            && Scale == other.Scale && Rotation == other.Rotation && Min == other.Min && Max == other.Max
            && Pivot == other.Pivot && Size == other.Size && Alpha == other.Alpha && Color == other.Color && Rendered == other.Rendered;
    }
    private readonly Node[] _nodes;
    private readonly State[] _from, _to;
    private bool _active, _hasTarget;
    private float _started, _duration;
    internal TownServiceMotion(Transform host, Transform[] originalNodes)
    {
        _nodes = new Node[originalNodes.Length + 1]; _from = new State[_nodes.Length]; _to = new State[_nodes.Length];
        for (int i = 0; i < _nodes.Length; i++)
        {
            Transform source = i == 0 ? host : originalNodes[i - 1];
            _nodes[i] = new Node { Transform = source, Rect = source as RectTransform,
                Group = source.GetComponent<CanvasGroup>(), Graphic = source.GetComponent<Graphic>() };
        }
    }
    internal void Reset()
    {
        if (_hasTarget)
            for (int i = 0; i < _nodes.Length; i++)
            { State current = Read(_nodes[i]); if (!current.Same(_to[i])) Write(_nodes[i], current, _to[i], 1f); }
        _active = false; _hasTarget = false;
    }
    internal void BeforeApply(float now)
    {
        Tick(now);
        for (int i = 0; i < _nodes.Length; i++) _from[i] = Read(_nodes[i]);
        // Binding skips unchanged target properties. Restore the previous complete target before
        // those writes, then blend from the currently displayed intermediate state afterwards.
        if (_hasTarget) for (int i = 0; i < _nodes.Length; i++)
            if (!_from[i].Same(_to[i])) Write(_nodes[i], _from[i], _to[i], 1f);
    }
    internal void AfterApply(float now, float sampleInterval)
    {
        _active = false; _hasTarget = true;
        for (int i = 0; i < _nodes.Length; i++)
        {
            _to[i] = Read(_nodes[i]);
            // Reopening/new parentage starts at its first actual owner state, never at an old
            // invisible pose. Subsequent sampled native appearance/movement is interpolated.
            if (!_from[i].Active || !_to[i].Active || _from[i].Parent != _to[i].Parent) _from[i] = _to[i];
            if (!_from[i].Same(_to[i])) _active = true;
        }
        _started = now; _duration = Mathf.Clamp(sampleInterval, 1f / 90f, .1f);
        if (_active) Tick(now);
    }
    internal void Tick(float now)
    {
        if (!_active) return;
        float t = Mathf.Clamp01((now - _started) / _duration);
        for (int i = 0; i < _nodes.Length; i++)
            if (!_from[i].Same(_to[i])) Write(_nodes[i], _from[i], _to[i], t);
        if (t >= 1f) _active = false;
    }
    private static State Read(Node node)
    {
        Transform source = node.Transform;
        var result = new State { Parent = source.parent, Active = source.gameObject.activeInHierarchy,
            Position = source.localPosition, Scale = source.localScale, Rotation = source.localRotation, Alpha = 1f };
        if (node.Rect != null)
        { result.Min = node.Rect.anchorMin; result.Max = node.Rect.anchorMax; result.Pivot = node.Rect.pivot; result.Size = node.Rect.sizeDelta; }
        // CanvasGroups may be appended by native transitions after the template was frozen.
        if (node.Group == null) node.Group = source.GetComponent<CanvasGroup>();
        if (node.Group != null && node.Group.enabled) result.Alpha = node.Group.alpha;
        if (node.Graphic != null) { result.Color = node.Graphic.color; result.Rendered = node.Graphic.canvasRenderer.GetColor(); }
        return result;
    }
    private static void Write(Node node, State from, State to, float t)
    {
        if (node.Transform == null) return;
        if (node.Rect != null)
        {
            if (from.Min != to.Min) node.Rect.anchorMin = Vector2.LerpUnclamped(from.Min, to.Min, t);
            if (from.Max != to.Max) node.Rect.anchorMax = Vector2.LerpUnclamped(from.Max, to.Max, t);
            if (from.Pivot != to.Pivot) node.Rect.pivot = Vector2.LerpUnclamped(from.Pivot, to.Pivot, t);
            if (from.Size != to.Size) node.Rect.sizeDelta = Vector2.LerpUnclamped(from.Size, to.Size, t);
        }
        if (from.Position != to.Position) node.Transform.localPosition = Vector3.LerpUnclamped(from.Position, to.Position, t);
        if (from.Rotation != to.Rotation) node.Transform.localRotation = Quaternion.SlerpUnclamped(from.Rotation, to.Rotation, t);
        if (from.Scale != to.Scale) node.Transform.localScale = Vector3.LerpUnclamped(from.Scale, to.Scale, t);
        if (node.Group != null && from.Alpha != to.Alpha) node.Group.alpha = Mathf.LerpUnclamped(from.Alpha, to.Alpha, t);
        if (node.Graphic != null)
        {
            if (from.Color != to.Color) node.Graphic.color = Color.LerpUnclamped(from.Color, to.Color, t);
            if (from.Rendered != to.Rendered) node.Graphic.canvasRenderer.SetColor(Color.LerpUnclamped(from.Rendered, to.Rendered, t));
        }
    }
}
