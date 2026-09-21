using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>One owner's full-size merchant workspace. The primary visitor uses the shared
/// counter; additional visitors receive furniture-only extensions behind the same NPC.
/// Only the owner resolves its position. Mirrors consume its actual pose/material output.</summary>
internal sealed class TownServiceWorkspace : IDisposable
{
    internal const float MoveSeconds = .22f;
    private readonly GameObject _root;
    private readonly List<Material> _materials = new();
    private readonly List<(int Id, string? Account, string? Name)> _roster = new();
    private readonly List<int> _ids = new();
    private Vector3 _from, _target;
    private float _moveStarted, _nextRoster, _visibility, _appliedVisibility = -1f;
    private bool _disposed;
    internal Transform Root => _root.transform;
    internal Transform FurnitureRoot { get; }
    internal IReadOnlyList<Material> Materials => _materials;
    internal static Transform? CounterTemplate => TownServiceAssets.Prefab("townmerchant")?.transform.Find("Counter");
    private static readonly int VisibilityId = Shader.PropertyToID("_TownVisibility");

    internal TownServiceWorkspace(Transform station)
    {
        Transform? template = CounterTemplate;
        if (station == null || template == null) throw new InvalidOperationException("Merchant workspace furniture is unavailable");
        _root = new GameObject("GloomhavenVR.TownService.Workspace");
        try
        {
            Root.SetParent(station, false);
            FurnitureRoot = UnityEngine.Object.Instantiate(template.gameObject, Root, false).transform;
            FurnitureRoot.name = template.name;
            foreach (Collider collider in FurnitureRoot.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            VRLayers.Apply(_root);
            var copies = new Dictionary<Material, Material>();
            foreach (MeshRenderer renderer in FurnitureRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material original = materials[i];
                    if (original == null) continue;
                    if (!copies.TryGetValue(original, out Material? copy))
                    {
                        copy = new Material(original); copies.Add(original, copy); _materials.Add(copy);
                    }
                    materials[i] = copy;
                }
                renderer.sharedMaterials = materials;
            }
            _target = Offset(ResolveSlot());
            _from = _target; Root.localPosition = _target;
            _moveStarted = Time.unscaledTime - MoveSeconds;
            _nextRoster = Time.unscaledTime + .25f;
            ApplyVisibility();
        }
        catch { Dispose(); throw; }
    }

    internal static Vector3 Offset(int slot)
    {
        if (slot < 0 || slot > 3) throw new ArgumentOutOfRangeException(nameof(slot));
        return slot == 0 ? Vector3.zero : new Vector3((slot - 2) * 1.8f, 0f, 2.2f);
    }

    private int ResolveSlot()
    {
        // Participants excludes connected users without an active controllable. Their default
        // index would otherwise be zero, colliding with the host. AllPlayers also includes flat
        // peers; reserving their ordinal avoids depending on local mod-handshake arrival order.
        _roster.Clear(); _ids.Clear(); NetPlayerActors.CollectRoster(_roster);
        int local = NetPlayerActors.LocalPlayerId();
        if (local <= 0) return 0;
        foreach (var player in _roster)
            if (player.Id > 0 && !_ids.Contains(player.Id)) _ids.Add(player.Id);
        if (!_ids.Contains(local)) _ids.Add(local);
        _ids.Sort();
        int slot = _ids.IndexOf(local);
        // The native session supports four users. Reject impossible registry data instead of
        // mapping two owners onto the same counter; presentation's normal fallback stays usable.
        if (slot > 3) throw new InvalidOperationException("Merchant workspace roster exceeds four users");
        return slot;
    }

    internal void Tick()
    {
        if (_disposed) return;
        float now = Time.unscaledTime;
        if (now >= _nextRoster)
        {
            _nextRoster = now + .25f;
            Vector3 next = Offset(ResolveSlot());
            if (next != _target)
            {
                _from = Root.localPosition; _target = next; _moveStarted = now;
            }
        }
        float t = Mathf.Clamp01((now - _moveStarted) / MoveSeconds);
        Root.localPosition = Vector3.LerpUnclamped(_from, _target, t * t * (3f - 2f * t));
        ApplyVisibility();
    }

    internal void SetVisibility(float value)
    {
        _visibility = Mathf.Clamp01(value);
        if (!_disposed) ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        // At ordinal zero the shared station already supplies this exact counter. A hidden
        // duplicate would z-fight if it ever rendered. On a rare move away from/to the primary
        // seat, dissolve the extension with its actual owner-authored movement.
        float value = _visibility * Mathf.Clamp01(Root.localPosition.magnitude / .5f);
        if (_appliedVisibility == value) return;
        _appliedVisibility = value;
        foreach (Material material in _materials) material.SetFloat(VisibilityId, value);
        FurnitureRoot.gameObject.SetActive(value > 0f);
        // Mesh property blocks are deliberately absent: the native presentation stream captures
        // these owned material values, including every intermediate dissolve value.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
        foreach (Material material in _materials) UnityEngine.Object.Destroy(material);
        _materials.Clear();
    }
}
