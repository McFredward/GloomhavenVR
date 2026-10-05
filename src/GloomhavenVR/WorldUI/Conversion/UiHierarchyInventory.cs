using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// An event-invalidated inventory of an adopted UI subtree. No Update, scene query or
/// transform polling runs on its observer components. Pool insertion, removal and reparenting
/// invalidate before the next LateUpdate; activation changes also wake cadence consumers.
/// Presentation state is never cached: callers still read sprites, visibility and animation
/// from the original components each frame. The inventory never belongs to a remote snapshot.
/// </summary>
internal sealed class UiHierarchyInventory : IDisposable
{
    private readonly Transform _root;
    private readonly List<Transform> _nodes = new(64);
    private readonly List<Observer> _observers = new(64);
    private bool _dirty = true;
    private bool _disposed;
    internal int Revision { get; private set; }
    // Read only by the synchronous converted-panel pass. Native reparenting and activation
    // can occur between panel callbacks without changing UIWindow's registry membership.
    internal static int HierarchyRevision { get; private set; }
    internal int Rebuilds { get; private set; }
    internal bool IsDirty => _dirty;
    internal Transform Root => _root;
    internal IReadOnlyList<Transform> Nodes => _nodes;

    internal UiHierarchyInventory(Transform root) { _root = root; }

    internal bool Refresh()
    {
        if (_disposed || _root == null || !_dirty) return false;
        _dirty = false;
        Detach();
        _nodes.Clear();
        _root.GetComponentsInChildren(includeInactive: true, _nodes);
        for (int i = 0; i < _nodes.Count; i++)
        {
            Transform node = _nodes[i];
            if (node == null) continue;
            Observer observer = node.GetComponent<Observer>();
            if (observer == null) observer = node.gameObject.AddComponent<Observer>();
            observer.Add(this);
            _observers.Add(observer);
        }
        Rebuilds++;
        return true;
    }

    private void Changed(bool hierarchy)
    {
        if (_disposed) return;
        if (hierarchy) _dirty = true;
        unchecked { Revision++; HierarchyRevision++; }
    }

    private void Detach()
    {
        for (int i = 0; i < _observers.Count; i++)
            if (_observers[i] != null) _observers[i].Remove(this);
        _observers.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
        _nodes.Clear();
    }

    // Not serialized: cloning a native widget must never copy subscriptions to its old
    // adopted panel. Empty observers are harmless pooled components, with no tick callback;
    // disposing an inventory immediately releases all strong owner references.
    internal sealed class Observer : MonoBehaviour
    {
        [NonSerialized] private readonly List<UiHierarchyInventory> _owners = new(1);
        private int _children;

        internal void Add(UiHierarchyInventory owner)
        {
            if (!_owners.Contains(owner)) _owners.Add(owner);
            _children = transform.childCount;
        }
        internal void Remove(UiHierarchyInventory owner) => _owners.Remove(owner);
        private void Notify(bool hierarchy)
        {
            for (int i = 0; i < _owners.Count; i++) _owners[i].Changed(hierarchy);
            _children = transform.childCount;
        }
        private void OnTransformChildrenChanged() => Notify(true);
        private void OnTransformParentChanged() => Notify(true);
        private void OnEnable() => Notify(_children != transform.childCount);
        private void OnDisable() => Notify(false);
        private void OnDestroy() => Notify(true);
    }
}
