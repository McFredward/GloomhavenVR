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
    private readonly State[] _from, _to, _before;
    private readonly float[] _nodeStarted, _nodeDuration, _nodeSampleTime;
    private readonly bool _continuousNativeEffects;
    private readonly bool _continuousDecisionFacing;
    private readonly bool _continuousVisitorMotion;
    private bool _active, _hasTarget;
    internal TownServiceMotion(Transform host, Transform[] originalNodes, string address = "")
    {
        _continuousNativeEffects = address.StartsWith("enchant.holder", StringComparison.Ordinal);
        _continuousDecisionFacing = address.StartsWith("item.confirm.part.", StringComparison.Ordinal)
            || address.StartsWith("enhance.confirm.part.", StringComparison.Ordinal);
        // A held temple purse and a merchant visitor's original item fan are
        // public moving props. Their native child animation uses this mirror;
        // verified hand roots also ride RemoteAvatar's rendered rig holder. The
        // 100 ms mechanical-control cap previously
        // finished their motion early when town packets arrived less often,
        // leaving a stationary purse/card between successive hand samples.
        _continuousVisitorMotion = address == "ritual.purse.held|"
            || address == "merchant.heldstock|"
            || address.StartsWith("temple.row|", StringComparison.Ordinal)
            || address.StartsWith("inspectionbody.", StringComparison.Ordinal)
            || IsVisitorItem(address);
        _nodes = new Node[originalNodes.Length + 1]; _from = new State[_nodes.Length]; _to = new State[_nodes.Length];
        _before = new State[_nodes.Length]; _nodeStarted = new float[_nodes.Length];
        _nodeDuration = new float[_nodes.Length]; _nodeSampleTime = new float[_nodes.Length];
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
    /// <summary>The independent native card clock owns the physical host/root
    /// pose. Keep only those channels in the interpolation state aligned with
    /// that rendered pose; otherwise an old enclosing canvas and a new child
    /// root tween apart as soon as the external clock finishes. Native child,
    /// color and alpha clocks continue independently.</summary>
    internal void AdoptExternalRootPose(bool applyTarget = false)
    {
        if (!_hasTarget) return;
        for (int i = 0; i < Math.Min(2, _nodes.Length); i++)
        {
            Node node = _nodes[i];
            if (node.Transform == null) continue;
            State pose = applyTarget ? _to[i] : Read(node);
            if (!applyTarget && i == 1 && node.Rect != null)
            {
                // The external TRS and its atomic rect dependency describe one
                // source instant. Interpolating that pivot separately displaces
                // the printed plane even though its physical root is correct.
                Vector3 position = node.Transform.position;
                node.Rect.anchorMin = _to[i].Min; node.Rect.anchorMax = _to[i].Max;
                node.Rect.pivot = _to[i].Pivot; node.Rect.sizeDelta = _to[i].Size;
                node.Transform.position = position; pose = Read(node);
            }
            if (applyTarget)
            {
                if (node.Rect != null)
                { node.Rect.anchorMin = pose.Min; node.Rect.anchorMax = pose.Max;
                  node.Rect.pivot = pose.Pivot; node.Rect.sizeDelta = pose.Size; }
                node.Transform.localPosition = pose.Position;
                node.Transform.localRotation = pose.Rotation; node.Transform.localScale = pose.Scale;
            }
            AdoptPose(ref _from[i], pose); AdoptPose(ref _to[i], pose);
        }
    }
    private static void AdoptPose(ref State state, State pose)
    {
        state.Parent = pose.Parent; state.Position = pose.Position; state.Rotation = pose.Rotation; state.Scale = pose.Scale;
        state.Min = pose.Min; state.Max = pose.Max; state.Pivot = pose.Pivot; state.Size = pose.Size;
    }
    internal void Reparent(Transform? parent)
    {
        Transform host = _nodes[0].Transform;
        Transform? previous = host.parent;
        if (previous == parent) return;
        // A retained census child keeps its original animation when its old
        // pooled holder retires. SetParent(true) preserves only the current
        // picture; the enclosing canvas's retained tween still stores positions
        // in the old holder. Rebase that one animation frame before disposal,
        // preserving clocks and unrelated native child/color interpolation.
        for (int i = 0; _hasTarget && i < _nodes.Length; i++)
        {
            if (_nodes[i].Transform != host) continue;
            _from[i] = ReparentState(_from[i], previous, parent);
            _to[i] = ReparentState(_to[i], previous, parent);
            _before[i] = ReparentState(_before[i], previous, parent);
        }
        host.SetParent(parent, true);
    }
    private static State ReparentState(State state, Transform? previous, Transform? parent)
    {
        Vector3 position = previous != null ? previous.TransformPoint(state.Position) : state.Position;
        Quaternion rotation = previous != null ? previous.rotation * state.Rotation : state.Rotation;
        Vector3 scale = previous != null ? Vector3.Scale(previous.lossyScale, state.Scale) : state.Scale;
        state.Parent = parent;
        state.Position = parent != null ? parent.InverseTransformPoint(position) : position;
        state.Rotation = parent != null ? Quaternion.Inverse(parent.rotation) * rotation : rotation;
        if (parent != null)
        { Vector3 basis = parent.lossyScale; state.Scale = new Vector3(scale.x / basis.x, scale.y / basis.y, scale.z / basis.z); }
        else state.Scale = scale;
        return state;
    }
    internal void BeforeApply(float now)
    {
        Tick(now);
        for (int i = 0; i < _nodes.Length; i++) _before[i] = Read(_nodes[i]);
        // Binding skips unchanged target properties. Restore the previous complete target before
        // those writes, then blend from the currently displayed intermediate state afterwards.
        if (_hasTarget) for (int i = 0; i < _nodes.Length; i++)
            if (!_before[i].Same(_to[i])) Write(_nodes[i], _before[i], _to[i], 1f);
    }
    internal void AfterApply(float now, float sampleInterval, bool sparseFan = false, float sourceSampleTime = -1f)
    {
        float duration = _continuousDecisionFacing || _continuousVisitorMotion
            ? Mathf.Clamp(sampleInterval * 1.1f, 1f / 90f, .25f)
            : Mathf.Clamp(sampleInterval, 1f / 90f, .1f);
        if (sparseFan && _continuousVisitorMotion)
            duration = Mathf.Clamp(sampleInterval * 1.1f, 1f / 90f, 1.5f);
        _active = false;
        for (int i = 0; i < _nodes.Length; i++)
        {
            State target = Read(_nodes[i]);
            if (!_hasTarget || !_to[i].Same(target))
            {
                _from[i] = _before[i]; _to[i] = target;
                if (!_from[i].Active || !target.Active || _from[i].Parent != target.Parent) _from[i] = target;
                float nodeInterval = _hasTarget && sourceSampleTime > _nodeSampleTime[i]
                    ? sourceSampleTime - _nodeSampleTime[i] : sampleInterval;
                State withoutRotation = _from[i]; withoutRotation.Rotation = target.Rotation;
                bool continuousSpin = _continuousNativeEffects && _from[i].Rotation != target.Rotation
                    && withoutRotation.Same(target);
                _nodeDuration[i] = continuousSpin
                    ? Mathf.Clamp(nodeInterval * 1.1f, 1f / 90f, 1.5f) : duration;
                _nodeStarted[i] = now; _nodeSampleTime[i] = sourceSampleTime;
            }
            // Root facing, color and independent property/header updates must
            // not restart an unchanged native ring's current interpolation.
            // BeforeApply restored target values solely for the binding pass;
            // resume each child from its own retained original sample clock.
            if (!_from[i].Same(_to[i]) && now - _nodeStarted[i] < _nodeDuration[i]) _active = true;
        }
        _hasTarget = true;
        Tick(now);
    }
    private static bool IsVisitorItem(string address)
    {
        if (!address.StartsWith("item.", StringComparison.Ordinal)) return false;
        int end = address.IndexOf('|');
        if (end <= 5) return false;
        for (int i = 5; i < end; i++) if (address[i] < '0' || address[i] > '9') return false;
        return true;
    }
    internal void Tick(float now)
    {
        if (!_active) return;
        bool pending = false;
        for (int i = 0; i < _nodes.Length; i++)
        {
            if (_from[i].Same(_to[i])) continue;
            float t = Mathf.Clamp01((now - _nodeStarted[i]) / _nodeDuration[i]);
            Write(_nodes[i], _from[i], _to[i], t);
            if (t < 1f) pending = true;
        }
        _active = pending;
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
        // Binding preserves the separately authored local position when native
        // anchors/pivot change. RectTransform's setters move that position again
        // during interpolation even if both sampled positions are identical.
        // Build658 could still drift sideways after the correct binding pass.
        // Restore the sampled position on each layout
        // tick as well as during an actual position tween.
        bool layoutMovesPosition = node.Rect != null
            && (from.Min != to.Min || from.Max != to.Max || from.Pivot != to.Pivot || from.Size != to.Size);
        if (from.Position != to.Position || layoutMovesPosition)
            node.Transform.localPosition = Vector3.LerpUnclamped(from.Position, to.Position, t);
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
