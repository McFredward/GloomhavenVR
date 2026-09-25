using System;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>A cosmetic station. It never owns a native transaction or continuation.</summary>
internal sealed class TownServiceStation : IDisposable
{
    private readonly GameObject _root;
    private readonly Animation? _animation;
    private readonly byte _service;
    private readonly TownServiceLighting _lighting;
    private readonly TownServiceDecor _decor;
    private readonly TownServiceGrounding _grounding;
    private readonly TownServiceFace _face;
    private readonly TownServiceActivityRig _activity;
    private readonly TownServiceActivityAudio _audio;
    private readonly TownServiceCloth _cloth;
    private bool _faceFailed, _activityFailed;
    private static readonly bool[] ActivityFailureReported = new bool[4];
    private static readonly bool[] FaceFailureReported = new bool[4];
    internal float ActorFloorOffset { get; private set; }
    internal float FurnitureBottom { get; private set; }
    private Transform? _room;
    private Vector3 _center;
    private float _scale;
    private bool _placed, _authorPose;
    private float _lightScale;
    private readonly Renderer[] _renderers;
    private readonly MaterialPropertyBlock _properties = new();
    private static readonly int VisibilityId = Shader.PropertyToID("_TownVisibility");
    private float _visibility = -1f;
    internal Transform Root => _root.transform;
    internal bool IsReady => _decor.Ready;
    internal Transform InteractionAnchor { get; }
    internal float GreetingDuration => _animation != null && _animation["Greeting"] != null
        ? _animation["Greeting"].length : 0f;

    private TownServiceStation(GameObject root, byte service, Vector3 center, float scale)
    {
        _root = root;
        _service = service;
        _center = center;
        _scale = scale;
        InteractionAnchor = root.transform.Find("InteractionAnchor")
            ?? throw new InvalidOperationException("Town station has no InteractionAnchor");
        PreserveActorDetail(root.transform.Find("Actor"));
        _animation = root.GetComponentInChildren<Animation>(true);
        _renderers = root.GetComponentsInChildren<Renderer>(true);
        _grounding = new TownServiceGrounding(root.transform);
        _face = new TownServiceFace(root.transform, service);
        _activity = new TownServiceActivityRig(root.transform, service);
        _audio = new TownServiceActivityAudio(root.transform, service);
        _lighting = new TownServiceLighting(root.transform, service);
        try { _decor = new TownServiceDecor(root.transform, service, _lighting);
            _cloth = new TownServiceCloth(root.transform, service); }
        catch { _lighting.Dispose(); _grounding.Dispose(); throw; }
    }

    private static void PreserveActorDetail(Transform? actor)
    {
        if (actor == null) return;
        // Build 547 switched between three independently simplified skin surfaces.
        // Close VR inspection exposed a hard silhouette/normal change, amplified by
        // vertex-lit practicals. New bundles contain only the full-detail actor;
        // this one-time compatibility path also fixes previously installed bundles.
        // Never change scenery, native figures, global LOD bias or light policy.
        foreach (LODGroup group in actor.GetComponentsInChildren<LODGroup>(true))
        {
            LOD[] levels = group.GetLODs();
            if (levels.Length == 0) continue;
            group.ForceLOD(0);
            group.enabled = false;
            foreach (LOD level in levels)
                foreach (Renderer renderer in level.renderers)
                {
                    if (renderer == null) continue;
                    renderer.enabled = Array.IndexOf(levels[0].renderers, renderer) >= 0;
                }
        }
    }

    internal static TownServiceStation? Create(byte service, Vector3 center, float scale)
    {
        string name = service == 1 ? "townmerchant" : service == 2 ? "townpriestess" : "townenchantress";
        GameObject? prefab = TownServiceAssets.Prefab(name);
        if (prefab == null) return null;
        GameObject? root = null;
        try
        {
            root = UnityEngine.Object.Instantiate(prefab);
            root.name = "GloomhavenVR.TownService." + service;
            if (!TownServicePlacement.TryResolve(service, center, scale, out Vector3 position, out Quaternion rotation))
            { UnityEngine.Object.Destroy(root); return null; }
            root.transform.SetPositionAndRotation(position, rotation);
            root.transform.localScale = Vector3.one * scale;
            // Decorative bodies and furniture must not intercept the laser or hand election.
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = VRLayers.ModLayer;
            return new TownServiceStation(root, service, center, scale);
        }
        catch
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            throw;
        }
    }

    internal void SetVisibility(float value)
    {
        if (_visibility == value) return;
        _visibility = value;
        _cloth.SetVisible(value > .01f);
        _lighting.SetVisibility(value);
        _decor.SetVisibility(value);
        foreach (Renderer renderer in _renderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(_properties);
            _properties.SetFloat(VisibilityId, value);
            renderer.SetPropertyBlock(_properties);
        }
    }

    /// <summary>Only the elected author may update pose; observers retain the published pose.</summary>
    internal void RefreshEnvironment(bool authorPose)
    {
        Transform? room = SkyAlternative.PlacedRoomRoot;
        bool changed = !_placed || room != _room || (authorPose && !_authorPose);
        if (authorPose && MapRoomDriver.TryGetParchmentFrame(out Vector3 center, out float scale))
        {
            changed |= center != _center || scale != _scale;
            if (changed && TownServicePlacement.TryResolve(_service, center, scale, out Vector3 position, out Quaternion rotation))
            {
                Root.SetPositionAndRotation(position, rotation);
                Root.localScale = Vector3.one * scale;
                _center = center; _scale = scale;
                _grounding.Resolve(out float actorFloor, out float furnitureBottom);
                SetGrounding(actorFloor, furnitureBottom);
            }
        }
        float lightScale = Root.lossyScale.x;
        if (changed || lightScale != _lightScale)
        { _lighting.Refresh(Root); _lightScale = lightScale; _room = room; _placed = true; }
        _authorPose = authorPose;
        _decor.Tick();
    }

    /// <summary>Ground geometry is part of the author's presentation, not a viewer preference.</summary>
    internal void SetGrounding(float actorFloor, float furnitureBottom)
    {
        ActorFloorOffset = actorFloor;
        FurnitureBottom = furnitureBottom;
        _grounding.Apply(actorFloor, furnitureBottom);
    }

    internal void Sample(string clip, float seconds)
    {
        _face.BeforeBodySample();
        _activity.BeforeBodySample();
        _decor.SetClock(seconds);
        if (_animation == null) return;
        AnimationState? state = _animation[clip];
        if (state == null) return;
        // Explicit sampling lets the shared cosmetic author supply the same phase to observers.
        _animation.Stop();
        state.enabled = true;
        state.weight = 1f;
        state.time = seconds;
        _animation.Sample();
        state.enabled = false;
    }

    internal void TickClothAuthor(float age, float dt) => _cloth.TickAuthor(age, dt, _visibility > .01f);
    internal void TickClothObserver(float age, float elapsed, in TownClothRunnerState first,
        in TownClothRunnerState second) => _cloth.TickObserver(age, elapsed, in first, in second, _visibility > .01f);
    internal TownClothRunnerState ClothFirst => _cloth.First;
    internal TownClothRunnerState ClothSecond => _cloth.Second;

    internal bool IsLocalVisitorNear(bool wasNear) => _face.IsLocalVisitorNear(wasNear);

    internal bool PrepareActivityAttention(bool wasEngaged)
    {
        if (_faceFailed) return false;
        try { return _face.PrepareActivityAttention(wasEngaged); }
        catch (Exception error) { FaceFailure(error); return false; }
    }
    internal void SampleActivity(in TownActivityVisual pose)
    {
        if (_activityFailed) return;
        try
        {
            if (!_activity.Ready) { _decor.SuspendActivity(); return; }
            _activity.Apply(in pose); _decor.SampleActivity(in pose);
        }
        catch (Exception error)
        {
            _activityFailed = true;
            try { _activity.Suspend(); _decor.SuspendActivity(); } catch { /* Cosmetic teardown must never gate a native visit. */ }
            if (_service >= 1 && _service <= 3 && !ActivityFailureReported[_service])
            {
                ActivityFailureReported[_service] = true;
                VRLog.Warn("TownServices", "NPC occupation disabled for service " + _service + ": " + error);
            }
        }
    }

    internal void SampleActivityAudio(int author, uint epoch, float clock, bool visible, in TownActivityVisual shown)
        => _audio.Tick(author, epoch, clock, Time.unscaledDeltaTime, visible && !_activityFailed, in shown);

    internal void SeedFace(TownFacePose pose, int author, float elapsed)
    {
        if (_faceFailed) return;
        try { _face.Seed(in pose, author, elapsed); }
        catch (Exception error) { FaceFailure(error); }
    }
    internal TownFacePose SampleFace(bool author, bool received, int authorId, in TownFacePose remote, float elapsed, float clock)
    {
        if (_faceFailed) return remote;
        try { return _face.Tick(author, received, authorId, in remote, elapsed, clock); }
        catch (Exception error) { FaceFailure(error); return remote; }
    }
    private void FaceFailure(Exception error)
    {
        _faceFailed = true; // A cosmetic failure must never gate the independent NPC visit target.
        if (_service < 1 || _service > 3 || FaceFailureReported[_service]) return;
        FaceFailureReported[_service] = true;
        VRLog.Warn("TownServices", "NPC facial animation disabled for service " + _service + ": " + error);
    }

    public void Dispose()
    {
        _cloth.Dispose();
        _audio.Dispose();
        _grounding.Dispose();
        _decor.Dispose();
        _lighting.Dispose();
        if (_root != null) UnityEngine.Object.Destroy(_root);
    }
}
