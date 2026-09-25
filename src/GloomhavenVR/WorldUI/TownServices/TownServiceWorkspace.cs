using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>One owner's full-size merchant workspace. The primary visitor uses the shared
/// counter; additional visitors receive full-size furniture on the free southern clearing ring.
/// Only the owner resolves its position. Mirrors consume its actual pose/material output.</summary>
internal sealed class TownServiceWorkspace : IDisposable
{
    internal const float MoveSeconds = .22f;
    internal sealed class Prop
    {
        internal string Key = string.Empty;
        internal Transform Root = null!;
    }
    private readonly byte _service;
    private readonly List<Prop> _props = new();
    private readonly Dictionary<Material, Material> _materialCopies = new();
    private readonly List<(Material Source, Material Copy)> _animatedMaterials = new();
    private float _nextProps;
    private static readonly int AnimationTimeId = Shader.PropertyToID("_TownAnimationTime");
    private readonly GameObject _root;
    private readonly List<Material> _materials = new();
    private readonly List<(int Id, string? Account, string? Name)> _roster = new();
    private readonly List<int> _ids = new();
    private readonly Transform _station;
    private readonly TownServiceGrounding _grounding;
    private TownServiceCloth? _cloth;
    private Vector3 _target, _center, _stationPosition;
    private Quaternion _targetRotation, _stationRotation;
    private Transform? _room;
    private float _scale, _yaw, _fadeFrom;
    private int _slot = -1;
    private bool _pending, _relocating, _switched, _shownPrimary;
    internal float RelocationVisibility { get; private set; } = 1f;
    internal bool InputAvailable => !_relocating;
    internal ulong RelocationRevision { get; private set; }
    private float _moveStarted, _nextRoster, _visibility, _appliedVisibility = -1f;
    private bool _disposed;
    internal Transform Root => _root.transform;
    internal Transform FurnitureRoot { get; }
    internal TownClothRunnerState ClothFirst => _shownPrimary ? default : _cloth?.First ?? default;
    internal TownClothRunnerState ClothSecond => _shownPrimary ? default : _cloth?.Second ?? default;
    internal bool HasCloth => !_shownPrimary && _cloth != null && _appliedVisibility > 0f;
    internal IReadOnlyList<Prop> Props => _props;
    internal IReadOnlyList<Material> Materials => _materials;
    internal static Transform? CounterTemplate => FurnitureTemplate(1);
    internal static Transform? FurnitureTemplate(byte service) => TownServiceAssets.Prefab(service == 1
        ? "townmerchant" : service == 2 ? "townpriestess" : "townenchantress")?.transform.Find(service == 1
            ? "Counter" : service == 2 ? "Shrine" : "Workbench");
    private static readonly int VisibilityId = Shader.PropertyToID("_TownVisibility");

    internal TownServiceWorkspace(Transform station, byte service = 1)
    {
        _service = service;
        Transform? template = FurnitureTemplate(service);
        if (station == null || template == null) throw new InvalidOperationException("Merchant workspace furniture is unavailable");
        _station = station;
        _root = new GameObject("GloomhavenVR.TownService.Workspace");
        try
        {
            Root.SetParent(station, false);
            FurnitureRoot = UnityEngine.Object.Instantiate(template.gameObject, Root, false).transform;
            FurnitureRoot.name = template.name;
            foreach (Collider collider in FurnitureRoot.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            VRLayers.Apply(_root);
            OwnMaterials(FurnitureRoot);
            if (service != 1) _cloth = new TownServiceCloth(Root, service);
            _grounding = new TownServiceGrounding(Root);
            if (!RefreshTarget(ResolveSlot(), true))
                throw new InvalidOperationException("Merchant workspace map frame is unavailable");
            ApplyTarget(); _pending = false; RefreshProps();
            _nextRoster = Time.unscaledTime + .25f;
            ApplyVisibility();
        }
        catch { Dispose(); throw; }
    }

    // The open/default environment retains a common reading frame. Custom rooms instead
    // use their own measured scenery frame and service-specific clearings.
    internal static float RingYaw(int slot)
    {
        TownServiceLayout.Resolve(TownServiceLayout.Environment.Open, 0, slot, out _, out float yaw);
        return yaw;
    }
    internal static Vector3 RingPosition(int slot)
    {
        TownServiceLayout.Resolve(TownServiceLayout.Environment.Open, 0, slot, out Vector3 position, out _);
        return position;
    }

    private bool RefreshTarget(int slot, bool force = false)
    {
        if (!MapRoomDriver.TryGetParchmentFrame(out Vector3 center, out float scale)
            || !MapRoomDriver.TrySolveSeat(out MapRoomSeat.Seat seat, out _)) return false;
        Transform? room = SkyAlternative.PlacedRoomRoot;
        Quaternion frame = TownServiceLayout.Frame(room, MapRoomDriver.ParchmentRenderer?.transform);
        float layoutYaw = frame.eulerAngles.y;
        if (!force && slot == _slot && room == _room && center == _center && scale == _scale
            && layoutYaw == _yaw && _station.position == _stationPosition && _station.rotation == _stationRotation) return true;
        _slot = slot; _room = room; _center = center; _scale = scale; _yaw = layoutYaw;
        _stationPosition = _station.position; _stationRotation = _station.rotation;
        if (slot == 0) { _target = _station.position; _targetRotation = _station.rotation; }
        else
        {
            TownServiceLayout.Resolve(TownServiceLayout.ForRoom(room), 0, slot,
                out Vector3 offset, out float heading);
            _target = center + frame * offset * scale;
            _target.y = room != null ? TownServicePlacement.GroundHeight(room, _target) : seat.FloorPosition.y;
            _targetRotation = frame * Quaternion.Euler(0f, heading, 0f);
        }
        _pending = true;
        return true;
    }

    private void ApplyTarget()
    {
        Root.SetPositionAndRotation(_target, _targetRotation);
        _grounding.Resolve(out float actorOffset, out float furnitureBottom);
        _grounding.Apply(actorOffset, furnitureBottom);
        _shownPrimary = _slot == 0; _pending = false;
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

    internal void Tick(bool mayRelocate = true)
    {
        if (_disposed) return;
        float now = Time.unscaledTime;
        if (now >= _nextProps) { _nextProps = now + .25f; RefreshProps(); }
        foreach (var material in _animatedMaterials)
            if (material.Source != null) material.Copy.SetFloat(AnimationTimeId, material.Source.GetFloat(AnimationTimeId));
        if (now >= _nextRoster)
        {
            _nextRoster = now + .25f;
            RefreshTarget(ResolveSlot());
        }
        // A held original card and its return flight retain the old rack until they finish.
        // Coalesce roster changes while waiting; never mutate a native selection or close it.
        if (_pending && mayRelocate && !_relocating)
        {
            _pending = false; _relocating = true; _switched = false;
            _fadeFrom = RelocationVisibility; _moveStarted = now;
        }
        if (_relocating)
        {
            float t = Mathf.Clamp01((now - _moveStarted) / MoveSeconds);
            if (!_switched && t >= .5f)
            {
                // Keep at least one completely invisible owner frame at the new pose. Sync
                // uses the revision as a durable presentation-generation boundary rather than
                // relying on delivery of that sample. Visible travel can cross other stands.
                RelocationVisibility = 0f; ApplyTarget();
                checked { RelocationRevision++; }
                _switched = true;
            }
            else if (!_switched) RelocationVisibility = Mathf.Lerp(_fadeFrom, 0f, t * 2f);
            else
            {
                RelocationVisibility = Mathf.Clamp01((t - .5f) * 2f);
                if (t >= 1f) { _relocating = false; RelocationVisibility = 1f; }
            }
        }
        ApplyVisibility();
        if (_appliedVisibility > 0f)
            _cloth?.TickAuthor(TownServicePresentation.SessionAge, Time.unscaledDeltaTime, true);
    }

    internal void SetVisibility(float value)
    {
        _visibility = Mathf.Clamp01(value);
        if (!_disposed) ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        // Ordinal zero already has the permanent counter. All other owner furniture uses
        // the same relocation opacity that Presentation supplies to its original widgets.
        float value = _shownPrimary ? 0f : _visibility * RelocationVisibility;
        if (_appliedVisibility == value) return;
        _appliedVisibility = value;
        foreach (Material material in _materials) material.SetFloat(VisibilityId, value);
        FurnitureRoot.gameObject.SetActive(value > 0f);
        _cloth?.SetVisible(value > 0f);
        foreach (Prop prop in _props) prop.Root.gameObject.SetActive(value > 0f);
        // Mesh property blocks are deliberately absent: the native presentation stream captures
        // these owned material values, including every intermediate dissolve value.
    }

    private void OwnMaterials(Transform root)
    {
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material original = materials[i];
                if (original == null) continue;
                if (!_materialCopies.TryGetValue(original, out Material? copy))
                {
                    copy = new Material(original); _materialCopies.Add(original, copy); _materials.Add(copy);
                    if (original.HasProperty(AnimationTimeId)) _animatedMaterials.Add((original, copy));
                }
                materials[i] = copy;
            }
            renderer.sharedMaterials = materials;
        }
    }

    private void RefreshProps()
    {
        if (_shownPrimary) return;
        for (int i = 0; i < TownServiceDecor.StaticPropCount(_service); i++)
        {
            if (!TownServiceDecor.TryStaticProp(_service, i, out Transform? source, out string key) || source == null) continue;
            bool exists = false;
            foreach (Prop prop in _props) if (prop.Key == key) { exists = true; break; }
            if (exists) continue;
            // These sources contain original rendering data only. Work tools and the NPC's
            // hand spell remain at the permanent resident; the visitor gets the static stand.
            Transform copy = UnityEngine.Object.Instantiate(source.gameObject, Root, false).transform;
            copy.localPosition = source.localPosition; copy.localRotation = source.localRotation; copy.localScale = source.localScale;
            OwnMaterials(copy); VRLayers.Apply(copy.gameObject);
            _props.Add(new Prop { Key = key, Root = copy });
            TownServiceWorkspacePractical.RebindClone(key, copy.gameObject);
            _appliedVisibility = -1f;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _grounding?.Dispose();
        _cloth?.Dispose();
        if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
        foreach (Material material in _materials) UnityEngine.Object.Destroy(material);
        _materials.Clear(); _materialCopies.Clear(); _animatedMaterials.Clear(); _props.Clear();
    }
}
