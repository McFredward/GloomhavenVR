using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core;

internal static partial class ScenarioTerrainBudget
{
    private sealed class Surface
    {
        internal readonly MeshRenderer Renderer;
        internal readonly int Identity;
        internal readonly MeshFilter Filter;
        internal readonly Mesh Original;
        internal readonly bool Floor;
        internal readonly int OriginalTriangles;
        internal readonly MaterialPropertyBlock Block = new();
        internal readonly MaterialPropertyBlock SlotBlock = new();
        internal Material[] Materials = Array.Empty<Material>();
        internal bool Distant;
        internal bool CheapLease;
        private Mesh? _exact, _target, _morph, _current;
        private Vector3[]? _from, _to, _vertices;
        private float _progress = 1f;
        private int _percent = 100, _requestedPercent = 100;
        private bool _masked;
        private Mesh? _countedMesh;
        private int _countedTriangles;
        private readonly GameObject _proxy;
        private readonly MeshRenderer _proxyRenderer;
        private readonly MeshFilter _proxyFilter;
        private readonly Transform _sourceTransform, _proxyTransform, _owner;
        private Matrix4x4 _sourcePose, _ownerPose;
        private bool _hasPose, _hasRendererState, _materialsDirty = true;
        private bool _hasPropertyDefaults, _copiedNativeProperties;
        private int _proxyMaterialSlots;
        private Mesh? _proxyMesh;
        private RendererState _rendererState;

        internal Surface(MeshRenderer renderer, MeshFilter filter, Transform owner)
        {
            Renderer = renderer; Identity = renderer.GetInstanceID(); Filter = filter; Original = filter.sharedMesh;
            Floor = FloorIdentity(Original);
            OriginalTriangles = TriangleCount(Original);
            _proxy = new GameObject("GloomhavenVR.TerrainProxy");
            _proxy.transform.SetParent(owner, false);
            _sourceTransform = renderer.transform; _proxyTransform = _proxy.transform; _owner = owner;
            _proxyFilter = _proxy.AddComponent<MeshFilter>();
            _proxyRenderer = _proxy.AddComponent<MeshRenderer>();
            _proxyRenderer.enabled = false;
        }
        internal bool Validate(Dictionary<Mesh, bool> meshes)
        {
            if (Renderer == null || Filter == null || Original == null || Filter.sharedMesh != Original) return false;
            // Bank admission reads the same native mesh metadata for every repeated
            // wall. Reuse only within one synchronous Update/camera invocation;
            // later eyes and native content changes re-read the genuine source.
            if (!meshes.TryGetValue(Original, out bool eligible))
            {
                eligible = _eligibleMesh?.Invoke(Original) == true;
                meshes[Original] = eligible;
            }
            return eligible;
        }
        internal bool WantsSubstitute(bool enabled) => (enabled && PerfConfig.CheapWallShadingOn)
            || (_current != null && _current != Original) || _progress < 1f;
        internal Mesh DrawMesh => _progress < 1f && _morph != null ? _morph : _current ?? Original;
        internal int DrawTriangles
        {
            get
            {
                Mesh mesh = DrawMesh;
                // Endpoint/topology identity changes only during owned preparation or
                // morphing. Keep triangle bookkeeping out of every settled camera draw.
                if (_countedMesh != mesh) { _countedMesh = mesh; _countedTriangles = TriangleCount(mesh); }
                return _countedTriangles;
            }
        }
        internal bool IsMasked => _masked && Renderer != null && Renderer.forceRenderingOff
            && _proxyRenderer != null && _proxyRenderer.enabled && _proxy.activeInHierarchy;
        private static int TriangleCount(Mesh mesh)
        {
            int count = 0;
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
                if (mesh.GetTopology(slot) == MeshTopology.Triangles) count += (int)mesh.GetIndexCount(slot) / 3;
            return count;
        }
        internal void EnsureSlots(int count)
        { if (Materials.Length != count) { Materials = new Material[count]; _materialsDirty = true; } }
        internal void SetMaterial(int slot, Material material)
        { if (Materials[slot] != material) { Materials[slot] = material; _materialsDirty = true; } }
        private static bool LiveSpecialEffect(MaterialPropertyBlock block) =>
            !block.isEmpty && (block.GetFloat("_AddVertexAnim") != 0f || block.GetFloat("_UseEmissiveMap") != 0f
            || block.GetFloat("_Diffuse_Emissive_On") != 0f);
        internal bool PrepareProxy(bool sharedOwner, Matrix4x4 sharedPose, Vector3 sharedScale)
        {
            Matrix4x4 native = _sourceTransform.localToWorldMatrix;
            Matrix4x4 owner = sharedOwner ? sharedPose : _owner.localToWorldMatrix;
            if (!_hasPose || !SameMatrix(native, _sourcePose) || !SameMatrix(owner, _ownerPose))
            {
                _hasPose = false;
                _proxyTransform.SetPositionAndRotation(_sourceTransform.position, _sourceTransform.rotation);
                // Keep the proxy outside every native cloning root. Unsupported host
                // shear/scale retains the original; no native transform is ever written.
                Vector3 parentScale = sharedOwner ? sharedScale : _owner.lossyScale;
                if (Mathf.Abs(parentScale.x) < .0001f || Mathf.Abs(parentScale.y) < .0001f || Mathf.Abs(parentScale.z) < .0001f) return false;
                Vector3 sourceScale = _sourceTransform.lossyScale;
                _proxyTransform.localScale = new Vector3(sourceScale.x / parentScale.x,
                    sourceScale.y / parentScale.y, sourceScale.z / parentScale.z);
                Matrix4x4 proxy = _proxyTransform.localToWorldMatrix;
                for (int i = 0; i < 16; i++) if (Mathf.Abs(native[i] - proxy[i]) > .0001f) return false;
                _sourcePose = native; _ownerPose = owner; _hasPose = true;
            }
            Mesh mesh = DrawMesh;
            if (_proxyMesh != mesh) { _proxyFilter.sharedMesh = mesh; _proxyMesh = mesh; }
            if (_materialsDirty)
            {
                // Clear previously owned index blocks before changing the private slot
                // count. A removed slot must not reappear with stale native artwork
                // when a later material change grows the array again.
                if (_copiedNativeProperties)
                    for (int slot = 0; slot < _proxyMaterialSlots; slot++) _proxyRenderer.SetPropertyBlock(null, slot);
                _proxyRenderer.sharedMaterials = Materials; _materialsDirty = false;
                _proxyMaterialSlots = Materials.Length; _hasPropertyDefaults = false;
            }
            CopyRendererState();
            // The source guard is fresh for each eye. A source without a native MPB
            // needs neither block reads nor any effect-value reads.
            // Only private output can be reused. The first empty invocation after
            // native blocks disappear must erase their renderer/slot overrides.
            if (!Renderer.HasPropertyBlock())
            {
                if (_copiedNativeProperties || !_hasPropertyDefaults)
                {
                    Block.Clear(); SetNeverFade(Block);
                    _proxyRenderer.SetPropertyBlock(Block);
                    for (int slot = 0; slot < Materials.Length; slot++) _proxyRenderer.SetPropertyBlock(null, slot);
                    _hasPropertyDefaults = true; _copiedNativeProperties = false;
                }
                return true;
            }
            _copiedNativeProperties = true;
            Renderer.GetPropertyBlock(Block);
            if (LiveSpecialEffect(Block)) return false;
            SetNeverFade(Block);
            _proxyRenderer.SetPropertyBlock(Block);
            for (int slot = 0; slot < Materials.Length; slot++)
            {
                Renderer.GetPropertyBlock(SlotBlock, slot);
                if (LiveSpecialEffect(SlotBlock)) return false;
                if (SlotBlock.isEmpty) _proxyRenderer.SetPropertyBlock(null, slot);
                else
                {
                    // Material-index blocks override the renderer-wide block in Unity.
                    SetNeverFade(SlotBlock);
                    _proxyRenderer.SetPropertyBlock(SlotBlock, slot);
                }
            }
            return true;
        }
        private void SetNeverFade(MaterialPropertyBlock block)
        {
            // Legacy cheap terrain and the global world shader use separate channels.
            // Set both on renderer and nonempty index blocks, whose values take
            // precedence in Unity. Walls explicitly retain the native fade route.
            block.SetFloat("_GHVRTerrainNeverFade", Floor ? 1f : 0f);
            block.SetFloat("_GHVRWorldNeverFade", Floor ? 1f : 0f);
        }
        private static bool SameMatrix(Matrix4x4 left, Matrix4x4 right)
        { for (int i = 0; i < 16; i++) if (left[i] != right[i]) return false; return true; }
        private struct RendererState
        {
            internal int Layer, SortingLayer, SortingOrder, Lightmap, RealtimeLightmap;
            internal uint RenderingLayer;
            internal bool ReceiveShadows, Occlusion;
            internal ShadowCastingMode Shadows;
            internal LightProbeUsage LightProbe;
            internal ReflectionProbeUsage ReflectionProbe;
            internal MotionVectorGenerationMode Motion;
            internal Transform? Anchor;
            internal GameObject? ProxyVolume;
            internal Vector4 LightmapOffset, RealtimeLightmapOffset;
        }
        private void CopyRendererState()
        {
            // Read every current native value on every camera. Only avoid identical
            // private Unity writes, which otherwise dirty the same renderer each eye.
            RendererState next = new()
            {
                Layer = Renderer.gameObject.layer, Shadows = Renderer.shadowCastingMode,
                ReceiveShadows = Renderer.receiveShadows, LightProbe = Renderer.lightProbeUsage,
                ReflectionProbe = Renderer.reflectionProbeUsage, Anchor = Renderer.probeAnchor,
                ProxyVolume = Renderer.lightProbeProxyVolumeOverride, SortingLayer = Renderer.sortingLayerID,
                SortingOrder = Renderer.sortingOrder, Occlusion = Renderer.allowOcclusionWhenDynamic,
                Lightmap = Renderer.lightmapIndex, LightmapOffset = Renderer.lightmapScaleOffset,
                RealtimeLightmap = Renderer.realtimeLightmapIndex, RealtimeLightmapOffset = Renderer.realtimeLightmapScaleOffset,
                Motion = Renderer.motionVectorGenerationMode, RenderingLayer = Renderer.renderingLayerMask
            };
            if (!_hasRendererState || next.Layer != _rendererState.Layer) _proxy.layer = next.Layer;
            if (!_hasRendererState || next.Shadows != _rendererState.Shadows) _proxyRenderer.shadowCastingMode = next.Shadows;
            if (!_hasRendererState || next.ReceiveShadows != _rendererState.ReceiveShadows) _proxyRenderer.receiveShadows = next.ReceiveShadows;
            if (!_hasRendererState || next.LightProbe != _rendererState.LightProbe) _proxyRenderer.lightProbeUsage = next.LightProbe;
            if (!_hasRendererState || next.ReflectionProbe != _rendererState.ReflectionProbe) _proxyRenderer.reflectionProbeUsage = next.ReflectionProbe;
            if (!_hasRendererState || next.Anchor != _rendererState.Anchor) _proxyRenderer.probeAnchor = next.Anchor;
            if (!_hasRendererState || next.ProxyVolume != _rendererState.ProxyVolume) _proxyRenderer.lightProbeProxyVolumeOverride = next.ProxyVolume;
            if (!_hasRendererState || next.SortingLayer != _rendererState.SortingLayer) _proxyRenderer.sortingLayerID = next.SortingLayer;
            if (!_hasRendererState || next.SortingOrder != _rendererState.SortingOrder) _proxyRenderer.sortingOrder = next.SortingOrder;
            if (!_hasRendererState || next.Occlusion != _rendererState.Occlusion) _proxyRenderer.allowOcclusionWhenDynamic = next.Occlusion;
            if (!_hasRendererState || next.Lightmap != _rendererState.Lightmap) _proxyRenderer.lightmapIndex = next.Lightmap;
            if (!_hasRendererState || !next.LightmapOffset.Equals(_rendererState.LightmapOffset)) _proxyRenderer.lightmapScaleOffset = next.LightmapOffset;
            if (!_hasRendererState || next.RealtimeLightmap != _rendererState.RealtimeLightmap) _proxyRenderer.realtimeLightmapIndex = next.RealtimeLightmap;
            if (!_hasRendererState || !next.RealtimeLightmapOffset.Equals(_rendererState.RealtimeLightmapOffset)) _proxyRenderer.realtimeLightmapScaleOffset = next.RealtimeLightmapOffset;
            if (!_hasRendererState || next.Motion != _rendererState.Motion) _proxyRenderer.motionVectorGenerationMode = next.Motion;
            if (!_hasRendererState || next.RenderingLayer != _rendererState.RenderingLayer) _proxyRenderer.renderingLayerMask = next.RenderingLayer;
            _rendererState = next; _hasRendererState = true;
        }
        internal void StepGeometry(int percent, float delta)
        {
            // Floors never fade or change geometry. Their exact native mesh remains the
            // only draw source, including while a separate cheap-lighting control is live.
            if (Floor) return;
            if (_exact == null && _lookup != null && _lookup(Original, 100, out Mesh exact)) _exact = exact;
            if (_exact == null || !_exact.isReadable) return;
            if (percent != _requestedPercent)
            {
                Mesh target = _exact;
                if (percent < 100 && (_lookup == null || !_lookup(Original, percent, out target))) return;
                if (target == null || !target.isReadable || target.vertexCount != _exact.vertexCount
                    || target.subMeshCount != _exact.subMeshCount) return;
                _requestedPercent = percent;
                // A bank may correctly fall back to its exact original if a seam-rich
                // mesh has no useful coarse tier. Keep its native renderer instead of
                // paying for an identical private draw or distorting without a saving.
                if (percent < 100 && TriangleCount(target) >= OriginalTriangles) { percent = 100; target = _exact; }
                _percent = percent;
                if (target == _exact && _current == null && _progress >= 1f) return;
                if (_target == target && _progress < 1f || _current == target && _progress >= 1f) return;
                if (_morph == null)
                {
                    _morph = UnityEngine.Object.Instantiate(_exact);
                    _morph.name = "GloomhavenVR.TerrainMorph"; _morph.MarkDynamic();
                }
                _from = (_progress < 1f ? _morph : _current ?? _exact).vertices;
                _to = target.vertices;
                if (_vertices == null) _vertices = new Vector3[_from.Length];
                _target = target; _progress = 0f;
                // Use the exact original topology throughout. Collapsed triangles become
                // degenerate continuously; removing only those at the endpoint cannot pop.
                for (int slot = 0; slot < _exact.subMeshCount; slot++)
                    _morph.SetIndices(_exact.GetIndices(slot), _exact.GetTopology(slot), slot, false);
            }
            if (_progress >= 1f || _morph == null || _from == null || _to == null || _vertices == null) return;
            // A hitch cannot skip every visible intermediate shape (standing fade rule).
            _progress = Mathf.Min(1f, _progress + Mathf.Clamp(delta, 0f, 1f / 30f) / .35f);
            float blend = _progress * _progress * (3f - 2f * _progress);
            for (int vertex = 0; vertex < _vertices.Length; vertex++)
                _vertices[vertex] = Vector3.LerpUnclamped(_from[vertex], _to[vertex], blend);
            _morph.vertices = _vertices; _morph.bounds = _exact.bounds;
            if (_progress >= 1f) _current = _percent >= 100 ? null : _target;
        }
        internal void Mask()
        {
            if (Renderer == null || Renderer.forceRenderingOff) return;
            _proxyRenderer.enabled = true;
            Renderer.forceRenderingOff = true; _masked = true;
        }
        internal void Unmask()
        {
            if (_proxyRenderer != null) _proxyRenderer.enabled = false;
            if (_masked && Renderer != null && Renderer.forceRenderingOff) Renderer.forceRenderingOff = false;
            _masked = false;
        }
        internal void Dispose()
        {
            Unmask();
            if (_morph != null) UnityEngine.Object.Destroy(_morph);
            if (_proxy != null) UnityEngine.Object.Destroy(_proxy);
            _morph = null; _current = null; _target = null; _exact = null;
            _from = null; _to = null; _vertices = null;
        }
    }
}
