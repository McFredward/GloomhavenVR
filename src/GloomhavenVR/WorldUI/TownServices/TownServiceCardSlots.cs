using System;
using System.Collections.Generic;
using UnityEngine;
using GloomhavenVR.Net;

namespace GloomhavenVR.WorldUI;

/// <summary>Original per-card enhancement points alongside the actual offered card.
/// These are separate from the character's campaign-wide enhancement capacity.</summary>
internal sealed class TownServiceCardSlots : IDisposable
{
    internal sealed class Point : IDisposable
    {
        internal readonly Transform Source;
        private readonly Transform _mount;
        private readonly RemoteWidgetMirror _mirror;
        private float _refreshAt;
        internal Transform? Content => _mirror.CloneOf(Source);
        internal Transform? CloneOf(Transform original) => _mirror.CloneOf(original);
        internal Point(UIEnhanceCardPoint source, Transform seat, int index)
        {
            Source = source.transform;
            _mount = new GameObject("Original card enhancement slot").transform;
            _mount.SetParent(seat, false);
            _mount.localPosition = new Vector3(.14f + index / 8 * .025f, .10f - index % 8 * .025f, 0f);
            _mirror = new RemoteWidgetMirror("TownCardSlot", _mount, .02f, .02f, Vector2.zero, mrBacking: false);
            try
            {
                if (!_mirror.Refresh(Source)) throw new InvalidOperationException("Original enhancement point is unavailable");
            }
            catch { Dispose(); throw; }
        }
        internal void Tick()
        {
            if (Time.unscaledTime >= _refreshAt)
            { _refreshAt = Time.unscaledTime + .1f; _mirror.Refresh(Source); }
            _mirror.TickLive();
        }
        public void Dispose() { _mirror.Destroy(); if (_mount != null) UnityEngine.Object.Destroy(_mount.gameObject); }
    }
    private readonly List<Point> _points = new();
    internal IReadOnlyList<Point> Points => _points;
    private UIEnhanceCardSlot? _slot;
    internal void Tick(TownServiceEnhancementHandoff handoff)
    {
        UIEnhanceCardSlot? slot = handoff.Card != null ? handoff.NativeSlot : null;
        if (slot != null)
            foreach (UIEnhanceCardPoint point in slot.assignedPoints)
                if (point == null) { slot = null; break; }
        bool changed = slot != _slot || slot != null && _points.Count != slot.assignedPoints.Count;
        if (!changed && slot != null)
            for (int i = 0; i < _points.Count; i++)
                if (slot.assignedPoints[i] == null || _points[i].Source != slot.assignedPoints[i].transform)
                { changed = true; break; }
        if (changed)
        {
            Clear(); _slot = slot;
            if (slot != null)
                for (int i = 0; i < slot.assignedPoints.Count; i++)
                    if (slot.assignedPoints[i] != null) _points.Add(new Point(slot.assignedPoints[i], handoff.Seat, i));
        }
        foreach (Point point in _points) point.Tick();
    }
    private void Clear() { foreach (Point point in _points) point.Dispose(); _points.Clear(); _slot = null; }
    public void Dispose() => Clear();
}
