using System;
using UnityEngine;

namespace GloomhavenVR.Core;

internal static partial class ScenarioTerrainBudget
{
    private sealed class Surface
    {
        internal readonly MeshRenderer Renderer;
        internal readonly MeshFilter Filter;
        internal readonly Mesh Original;
        internal readonly bool Floor;
        internal readonly int OriginalTriangles;
        internal readonly MaterialPropertyBlock Block = new();
        internal readonly MaterialPropertyBlock SlotBlock = new();
        internal Material[] Materials = Array.Empty<Material>();
        internal bool Distant;
        private Mesh? _exact, _target, _morph, _current;
        private Vector3[]? _from, _to, _vertices;
        private float _progress = 1f;
        private int _percent = 100, _requestedPercent = 100;
        private bool _masked;
        private readonly GameObject _proxy;
        private readonly MeshRenderer _proxyRenderer;
        private readonly MeshFilter _proxyFilter;

        internal Surface(MeshRenderer renderer, MeshFilter filter, Transform owner)
        {
            Renderer = renderer; Filter = filter; Original = filter.sharedMesh;
            Floor = FloorIdentity(Original);
            OriginalTriangles = TriangleCount(Original);
            _proxy = new GameObject("GloomhavenVR.TerrainProxy");
            _proxy.transform.SetParent(owner, false);
            _proxyFilter = _proxy.AddComponent<MeshFilter>();
            _proxyRenderer = _proxy.AddComponent<MeshRenderer>();
            _proxyRenderer.enabled = false;
        }
        internal bool Validate() => Renderer != null && Filter != null && Original != null
            && Filter.sharedMesh == Original && _eligibleMesh?.Invoke(Original) == true;
        internal bool WantsSubstitute(bool enabled) => (enabled && PerfConfig.CheapWallShadingOn)
            || (_current != null && _current != Original) || _progress < 1f;
        internal Mesh DrawMesh => _progress < 1f && _morph != null ? _morph : _current ?? Original;
        internal int DrawTriangles => TriangleCount(DrawMesh);
        private static int TriangleCount(Mesh mesh)
        {
            int count = 0;
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
                if (mesh.GetTopology(slot) == MeshTopology.Triangles) count += (int)mesh.GetIndexCount(slot) / 3;
            return count;
        }
        internal void EnsureSlots(int count) { if (Materials.Length != count) Materials = new Material[count]; }
        private static bool LiveSpecialEffect(MaterialPropertyBlock block) =>
            block.GetFloat("_AddVertexAnim") != 0f || block.GetFloat("_UseEmissiveMap") != 0f
            || block.GetFloat("_Diffuse_Emissive_On") != 0f;
        internal bool PrepareProxy()
        {
            Transform source = Renderer.transform;
            _proxy.transform.SetPositionAndRotation(source.position, source.rotation);
            // Keep the proxy outside every native cloning root. A non-identity mod host
            // or native shear cannot be faithfully expressed by this TRS path: fail open.
            Vector3 parentScale = _proxy.transform.parent.lossyScale;
            if (Mathf.Abs(parentScale.x) < .0001f || Mathf.Abs(parentScale.y) < .0001f || Mathf.Abs(parentScale.z) < .0001f) return false;
            _proxy.transform.localScale = new Vector3(source.lossyScale.x / parentScale.x,
                source.lossyScale.y / parentScale.y, source.lossyScale.z / parentScale.z);
            Matrix4x4 native = source.localToWorldMatrix, proxy = _proxy.transform.localToWorldMatrix;
            for (int i = 0; i < 16; i++) if (Mathf.Abs(native[i] - proxy[i]) > .0001f) return false;
            _proxy.layer = Renderer.gameObject.layer;
            _proxyFilter.sharedMesh = DrawMesh;
            _proxyRenderer.sharedMaterials = Materials;
            _proxyRenderer.shadowCastingMode = Renderer.shadowCastingMode;
            _proxyRenderer.receiveShadows = Renderer.receiveShadows;
            _proxyRenderer.lightProbeUsage = Renderer.lightProbeUsage;
            _proxyRenderer.reflectionProbeUsage = Renderer.reflectionProbeUsage;
            _proxyRenderer.probeAnchor = Renderer.probeAnchor;
            _proxyRenderer.lightProbeProxyVolumeOverride = Renderer.lightProbeProxyVolumeOverride;
            _proxyRenderer.sortingLayerID = Renderer.sortingLayerID;
            _proxyRenderer.sortingOrder = Renderer.sortingOrder;
            _proxyRenderer.allowOcclusionWhenDynamic = Renderer.allowOcclusionWhenDynamic;
            _proxyRenderer.lightmapIndex = Renderer.lightmapIndex;
            _proxyRenderer.lightmapScaleOffset = Renderer.lightmapScaleOffset;
            _proxyRenderer.realtimeLightmapIndex = Renderer.realtimeLightmapIndex;
            _proxyRenderer.realtimeLightmapScaleOffset = Renderer.realtimeLightmapScaleOffset;
            _proxyRenderer.motionVectorGenerationMode = Renderer.motionVectorGenerationMode;
            _proxyRenderer.renderingLayerMask = Renderer.renderingLayerMask;
            Renderer.GetPropertyBlock(Block);
            if (LiveSpecialEffect(Block)) return false;
            Block.SetFloat("_GHVRTerrainNeverFade", Floor ? 1f : 0f);
            _proxyRenderer.SetPropertyBlock(Block);
            for (int slot = 0; slot < Materials.Length; slot++)
            {
                Renderer.GetPropertyBlock(SlotBlock, slot);
                if (LiveSpecialEffect(SlotBlock)) return false;
                if (SlotBlock.isEmpty) _proxyRenderer.SetPropertyBlock(null, slot);
                else
                {
                    // Material-index blocks override the renderer-wide block in Unity.
                    SlotBlock.SetFloat("_GHVRTerrainNeverFade", Floor ? 1f : 0f);
                    _proxyRenderer.SetPropertyBlock(SlotBlock, slot);
                }
            }
            return true;
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
