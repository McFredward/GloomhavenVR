using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core;

/// <summary>
/// Optional visible idle presentation: a private baked pose stands in for an audited native
/// skin between samples. Camera-only forceRenderingOff leases make CullUpdateTransforms skip
/// bone writes while Unity still advances the original state clock. Unity2021 calibration
/// proves this distinction; neither Animator.enabled/speed nor native renderer.enabled changes.
/// Native meshes, material slots and hierarchy remain unchanged, including during Instantiate.
/// Actions/holding/state changes restore before dispatch; unsupported skins remain native.
/// </summary>
internal sealed class ScenarioVisibleIdleSnapshot : IDisposable
{
    private sealed class LodLease
    {
        internal LODGroup Group = null!;
        internal LOD[] Original = null!, Replacement = null!;
        internal bool Enabled, Masked;
        internal float Size;
        internal Vector3 Reference;
        internal bool Matches()
            => Group != null && Group.enabled == Enabled && Group.fadeMode == LODFadeMode.None
                && Group.size == Size && Group.localReferencePoint == Reference
                && SameTable(Group.GetLODs(), Masked ? Replacement : Original);
        internal void Release()
        {
            // Preserve a late native/foreign table. The same group keeps its hidden
            // ForceLOD state: never ForceLOD, disable, clone or recalculate its bounds.
            if (Masked && Group != null && SameTable(Group.GetLODs(), Replacement)) Group.SetLODs(Original);
            Masked = false;
        }
    }

    private static bool SameTable(LOD[] left, LOD[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i].screenRelativeTransitionHeight != right[i].screenRelativeTransitionHeight
                || left[i].fadeTransitionWidth != right[i].fadeTransitionWidth
                || left[i].renderers.Length != right[i].renderers.Length) return false;
            for (int j = 0; j < left[i].renderers.Length; j++)
                if (left[i].renderers[j] != right[i].renderers[j]) return false;
        }
        return true;
    }
    private sealed class Surface
    {
        internal SkinnedMeshRenderer Source = null!;
        internal Mesh Original = null!, Baked = null!;
        internal MeshRenderer Proxy = null!;
        internal Material[] Materials = Array.Empty<Material>();
        internal Cloth? Cloth;
        internal SkinQuality SkinQuality;
        internal SkinWeights SkinWeights;
        internal bool Masked;
        internal bool Matches(List<Material> current)
        {
            if (Source == null || Proxy == null || Source.sharedMesh != Original
                || !Source.enabled || !Source.gameObject.activeInHierarchy || Source.HasPropertyBlock()
                || Source.GetComponent<Cloth>() != Cloth || (Cloth != null && Cloth.enabled)
                || Source.quality != SkinQuality || QualitySettings.skinWeights != SkinWeights
                || Source.lightmapIndex >= 0
                || Source.shadowCastingMode != Proxy.shadowCastingMode || Source.receiveShadows != Proxy.receiveShadows
                || Source.lightProbeUsage != Proxy.lightProbeUsage || Source.reflectionProbeUsage != Proxy.reflectionProbeUsage
                || Source.probeAnchor != Proxy.probeAnchor || Source.lightProbeProxyVolumeOverride != Proxy.lightProbeProxyVolumeOverride
                || Source.renderingLayerMask != Proxy.renderingLayerMask
                || Source.motionVectorGenerationMode != Proxy.motionVectorGenerationMode
                || Source.sortingLayerID != Proxy.sortingLayerID || Source.sortingOrder != Proxy.sortingOrder
                || Source.allowOcclusionWhenDynamic != Proxy.allowOcclusionWhenDynamic) return false;
            current.Clear(); Source.GetSharedMaterials(current);
            if (current.Count != Materials.Length) return false;
            for (int i = 0; i < current.Count; i++) if (current[i] != Materials[i]) return false;
            return Masked ? Source.forceRenderingOff : !Source.forceRenderingOff;
        }
        internal void Release()
        {
            if (Proxy != null) Proxy.enabled = false;
            if (Masked && Source != null && Source.forceRenderingOff) Source.forceRenderingOff = false;
            Masked = false;
        }
    }

    private readonly ActorBarPose _pose;
    private readonly Transform _host;
    private readonly LODGroup[] _nativeLods;
    private readonly List<SkinnedMeshRenderer> _sources = new(8);
    private readonly List<Surface> _surfaces = new(8);
    private readonly List<LodLease> _lodLeases = new(4);
    private readonly List<Material> _materialScratch = new(8);
    private float _nextSample, _interval;
    private int _sampleFrame = -1;
    private bool _active, _disposed;
    internal bool HasPose => _surfaces.Count != 0;
    internal int Samples { get; private set; }
    internal bool IsMasked { get; private set; }
    internal int MaskedSurfaceCount => IsMasked ? _surfaces.Count : 0;
    internal bool HasMaskedClothApproximation => IsMasked && _pose.HasIdleCloth
        && PerfConfig.VisibleIdleClothApproximation;
    internal bool AwaitingNativePose => _sampleFrame >= 0;
    internal bool HasActiveNativeLod
    {
        get
        {
            foreach (LODGroup group in _nativeLods)
                // Crossfade uses per-renderer native fade state. It has no exact private
                // proxy proof yet and stays native. Ordinary native LOD/ForceLOD selection
                // stays on the SAME group through a reversible camera table lease.
                if (group != null && group.enabled && group.gameObject.activeInHierarchy
                    && group.fadeMode != LODFadeMode.None) return true;
            return false;
        }
    }

    internal ScenarioVisibleIdleSnapshot(ActorBarPose pose, Transform host)
    {
        _pose = pose; _host = host;
        // Keep original groups, including hidden ForceLOD selection. The renderer table
        // is leased only while this camera renders and restored before cloning/actions.
        _nativeLods = pose.NativeRoot.GetComponentsInChildren<LODGroup>(true);
    }

    internal void Tick(bool eligible, float interval, bool requestSample = true)
    {
        _active = eligible && interval > 0f && !_disposed && !HasActiveNativeLod && !_pose.HasActiveIdleCloth;
        _interval = interval;
        if (!_active) { Release(); _sampleFrame = -1; return; }
        if (requestSample) RequestSample(interval);
    }

    internal void RequestSample(float interval)
    {
        if (_active && _sampleFrame < 0 && Time.unscaledTime >= _nextSample)
        {
            // One native camera render first makes the skin visible. Its following native
            // Animator evaluation produces the next pose; sample only after that evaluation.
            Release(); _sampleFrame = Time.frameCount;
            _nextSample = Time.unscaledTime + interval;
        }
    }

    internal void AfterNativePose()
    {
        if (!_active || _sampleFrame < 0 || Time.frameCount <= _sampleFrame) return;
        using var scope = PerfMonitor.Scope("Figure.VisibleIdleBake");
        try
        {
            if (HasActiveNativeLod || _pose.HasActiveIdleCloth
                || !_pose.CopyIdleSkinSources(_sources) || _sources.Count == 0) { Reset(); return; }
            bool same = _sources.Count == _surfaces.Count;
            for (int i = 0; same && i < _sources.Count; i++)
                same = _surfaces[i].Source == _sources[i] && _surfaces[i].Matches(_materialScratch);
            if (!same) Rebuild();
            if (_surfaces.Count == 0) { _active = false; _sampleFrame = -1; return; }
            foreach (Surface surface in _surfaces)
            {
                surface.Source.BakeMesh(surface.Baked, false);
                surface.Baked.bounds = surface.Source.localBounds;
            }
            Samples++; _sampleFrame = -1;
            _nextSample = Time.unscaledTime + _interval;
        }
        catch { Reset(); throw; }
    }

    private void Rebuild()
    {
        ResetSurfaces();
        int vertices = 0;
        foreach (SkinnedMeshRenderer source in _sources)
        {
            Mesh mesh = source != null ? source.sharedMesh : null!;
            if (source == null || mesh == null || mesh.vertexCount == 0 || source.HasPropertyBlock()
                || source.forceRenderingOff || !source.enabled || !source.gameObject.activeInHierarchy
                || source.lightmapIndex >= 0
                || (vertices += mesh.vertexCount) > 60000) { ResetSurfaces(); return; }
            var proxyObject = new GameObject("GloomhavenVR.VisibleIdlePose");
            proxyObject.transform.SetParent(_host, false);
            proxyObject.hideFlags = HideFlags.HideAndDontSave;
            var baked = new Mesh { name = "GloomhavenVR.VisibleIdlePose", indexFormat = mesh.indexFormat };
            baked.MarkDynamic(); proxyObject.AddComponent<MeshFilter>().sharedMesh = baked;
            MeshRenderer proxy = proxyObject.AddComponent<MeshRenderer>(); proxy.enabled = false;
            proxy.sharedMaterials = source.sharedMaterials;
            proxy.shadowCastingMode = source.shadowCastingMode; proxy.receiveShadows = source.receiveShadows;
            proxy.lightProbeUsage = source.lightProbeUsage; proxy.reflectionProbeUsage = source.reflectionProbeUsage;
            proxy.probeAnchor = source.probeAnchor; proxy.lightProbeProxyVolumeOverride = source.lightProbeProxyVolumeOverride;
            proxy.renderingLayerMask = source.renderingLayerMask;
            proxy.motionVectorGenerationMode = source.motionVectorGenerationMode;
            proxy.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            proxy.sortingLayerID = source.sortingLayerID; proxy.sortingOrder = source.sortingOrder;
            _surfaces.Add(new Surface { Source = source, Original = mesh, Baked = baked,
                Proxy = proxy, Materials = proxy.sharedMaterials, Cloth = source.GetComponent<Cloth>(),
                SkinQuality = source.quality, SkinWeights = QualitySettings.skinWeights });
        }
        foreach (LODGroup group in _nativeLods)
        {
            if (group == null) continue;
            LOD[] original = group.GetLODs();
            var replacement = new LOD[original.Length];
            bool changed = false;
            for (int level = 0; level < original.Length; level++)
            {
                Renderer[] renderers = (Renderer[])original[level].renderers.Clone();
                for (int index = 0; index < renderers.Length; index++)
                    foreach (Surface surface in _surfaces)
                        if (renderers[index] == surface.Source)
                        { renderers[index] = surface.Proxy; changed = true; break; }
                replacement[level] = new LOD(original[level].screenRelativeTransitionHeight, renderers)
                    { fadeTransitionWidth = original[level].fadeTransitionWidth };
            }
            if (changed) _lodLeases.Add(new LodLease { Group = group, Original = original, Replacement = replacement,
                Enabled = group.enabled, Size = group.size, Reference = group.localReferencePoint });
        }
    }

    internal void BeforeCamera(bool admitted)
    {
        Release();
        if (!admitted || !_active || _sampleFrame >= 0 || !HasPose || !_pose.IsEventFreeNativeIdle()) return;
        if (HasActiveNativeLod || _pose.HasActiveIdleCloth) { Reset(); return; }
        foreach (Surface surface in _surfaces) if (!surface.Matches(_materialScratch)) { Reset(); return; }
        foreach (LodLease lod in _lodLeases) if (!lod.Matches()) { Reset(); return; }
        foreach (Surface surface in _surfaces)
        {
            Transform source = surface.Source.transform, proxy = surface.Proxy.transform;
            // An arbitrary sheared hierarchy cannot be represented by one TRS proxy.
            Matrix4x4 matrix = source.localToWorldMatrix;
            Vector3 scale = source.lossyScale;
            Matrix4x4 trs = Matrix4x4.TRS(source.position, source.rotation, scale);
            for (int element = 0; element < 16; element++)
                if (Mathf.Abs(matrix[element] - trs[element]) > .0001f) { Reset(); return; }
            proxy.SetPositionAndRotation(source.position, source.rotation); proxy.localScale = scale;
            surface.Proxy.gameObject.layer = surface.Source.gameObject.layer;
        }
        foreach (Surface surface in _surfaces)
        {
            surface.Source.forceRenderingOff = true; surface.Masked = true; surface.Proxy.enabled = true;
        }
        foreach (LodLease lod in _lodLeases)
            if (lod.Enabled && lod.Group.gameObject.activeInHierarchy)
            { lod.Group.SetLODs(lod.Replacement); lod.Masked = true; }
        IsMasked = true;
    }

    internal void ValidateCameraLease()
    {
        if (!IsMasked) return;
        if (!_pose.IsEventFreeNativeIdle() || HasActiveNativeLod || _pose.HasActiveIdleCloth)
        { Reset(); return; }
        foreach (Surface surface in _surfaces) if (!surface.Matches(_materialScratch)) { Reset(); return; }
        foreach (LodLease lod in _lodLeases) if (!lod.Matches()) { Reset(); return; }
    }

    internal void Release()
    {
        foreach (LodLease lod in _lodLeases) lod.Release();
        foreach (Surface surface in _surfaces) surface.Release();
        IsMasked = false;
    }
    private void ResetSurfaces()
    {
        Release();
        foreach (Surface surface in _surfaces)
        {
            if (surface.Proxy != null) UnityEngine.Object.Destroy(surface.Proxy.gameObject);
            if (surface.Baked != null) UnityEngine.Object.Destroy(surface.Baked);
        }
        _surfaces.Clear();
        _lodLeases.Clear();
    }
    internal void Reset() { ResetSurfaces(); _sampleFrame = -1; _active = false; }
    public void Dispose() { Reset(); _sources.Clear(); _materialScratch.Clear(); _disposed = true; }
}
