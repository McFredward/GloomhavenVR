using System;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>The game's replicated player identity above an occupied resident.
/// Proximity never occupies a resident. The reliable physical-offer grant is the
/// only author; the temple does not have an exclusive visitor.</summary>
internal sealed class TownServiceOccupationBadge : IDisposable
{
    private readonly byte _service;
    private readonly Transform _station;
    private readonly Transform? _head;
    private OwnerTag? _tag;
    private int _owner;

    internal TownServiceOccupationBadge(byte service, Transform station)
    {
        _service = service;
        _station = station;
        Transform? actor = station.Find("Actor");
        if (actor == null) return;
        // Resolve once from the resident's own rig, never from a decoration or
        // from another visitor's local environment.
        foreach (Transform bone in actor.GetComponentsInChildren<Transform>(true))
            if (bone.name == "Head") { _head = bone; break; }
    }

    internal void Tick(bool visible)
    {
        int owner = visible && (_service == 1 || _service == 3)
            ? TownServiceGrantSync.GrantedOwner(_service) : 0;
        if (owner != _owner)
        {
            Retire();
            _owner = owner;
            if (owner > 0) _tag = new OwnerTag(owner, _station, Vector3.zero);
        }
        if (_tag == null || _tag.Root == null) return;
        Transform root = _tag.Root;
        root.position = _head != null
            ? _head.position + Vector3.up * (.24f * Mathf.Abs(_station.lossyScale.y))
            : _station.TransformPoint(new Vector3(0f, 1.95f, 0f));
        // Reuse the approved board identity, depth ordering and measured text fit.
        // Occupancy remains legible even when cosmetic player tags are disabled.
        _tag.Tick(required: true);
    }

    private void Retire()
    {
        // Unity destruction is deferred; the released player must disappear on
        // this frame, before the next offer can show its new owner.
        if (_tag?.Root != null) _tag.Root.gameObject.SetActive(false);
        _tag?.Destroy();
        _tag = null;
    }

    public void Dispose() { Retire(); _owner = 0; }
}
