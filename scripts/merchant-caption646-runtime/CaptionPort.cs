using System;
using System.Collections.Generic;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

namespace GloomhavenVR.Net;

// Declared boundary: the existing source-to-inert-clone mirror and its pair lookup.
// The real merchant caption helper, capture/binding, codec, sprites and Unity render
// execute unchanged. The fixture uses Binding for the original-property copy so
// native fields genuinely overwrite the caller's layout on every refresh.
internal sealed class RemoteWidgetMirror : IDisposable
{
    private readonly Transform _source;
    private TownServiceBinding _sourceBinding, _cloneBinding;
    internal int RebuildStamp { get; private set; }
    internal Transform Root => _cloneBinding.Root;
    internal RemoteWidgetMirror(Transform source, Transform parent)
    {
        _source = source; _sourceBinding = new TownServiceBinding(source);
        _cloneBinding = Create(parent);
    }
    private TownServiceBinding Create(Transform parent)
    {
        var copy = UnityEngine.Object.Instantiate(_source.gameObject, parent, false);
        copy.SetActive(false); TownServiceNeutralize.Apply(copy); copy.SetActive(true);
        RebuildStamp++;
        return new TownServiceBinding(copy.transform);
    }
    internal void Rebuild()
    {
        Transform parent = Root.parent; UnityEngine.Object.DestroyImmediate(Root.gameObject);
        _cloneBinding.Dispose(); _cloneBinding = Create(parent);
    }
    internal Transform? CloneOf(Transform? source)
    {
        if (source == null) return null;
        if (source == _source) return Root;
        string path = source.name;
        for (Transform node = source.parent; node != _source; node = node.parent)
            path = node.name + "/" + path;
        return Root.Find(path);
    }
    internal void TickLive()
    {
        var frame = new TownServiceFrame { Service = 1, Session = 646, Module = 11, Template = 1,
            Structure = _sourceBinding.Structure, Visible = true, Nodes = TownServiceDelta.Retain(new TownServiceFrame { Nodes = _sourceBinding.Read(TownServiceMirror.Assets) }).Nodes,
            Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } };
        _cloneBinding.Apply(frame, TownServiceMirror.Assets);
    }
    public void Dispose() { _sourceBinding.Dispose(); _cloneBinding.Dispose(); }
}
