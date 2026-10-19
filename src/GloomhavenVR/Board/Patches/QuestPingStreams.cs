using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Board.Patches;

/// <summary>
/// Submit only the original ping's two billboards through ordinary COLOR-bearing
/// meshes. Stream narrowing alone still corrupts Adreno ping pixels (B711).
/// BakeMesh snapshots Unity's live simulation and authored alignment; it does
/// not recreate particles, alter their shader, or move their animation clock.
/// </summary>
internal sealed class QuestPingStreams : MonoBehaviour
{
    private static readonly FieldInfo? Assigned = typeof(PingManager).GetField("assigned", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static FieldInfo? _highlight;
    private static readonly List<QuestPingStreams> Live = new();
    private readonly List<Binding> _bindings = new(2);
    private static bool _failureReported;
    private static int _diagnostics;
    private const int MaximumVertices = 4096;

    private sealed class Binding
    {
        internal readonly ParticleSystemRenderer Source;
        internal readonly Material Material;
        internal readonly Shader Shader;
        internal readonly List<ParticleSystemVertexStream> Streams = new(4);
        internal readonly Mesh Snapshot = new() { name = "GloomhavenVR.Ping.Snapshot" };
        internal readonly Mesh Upload = new() { name = "GloomhavenVR.Ping.Upload" };
        internal readonly MeshRenderer Draw;
        internal readonly List<Vector3> Positions = new();
        internal readonly List<Vector2> Uv = new();
        internal readonly List<Color> Colors = new();
        internal readonly List<int> Indices = new();
        internal readonly MaterialPropertyBlock Properties = new();
        internal bool Claimed;
        internal bool Failed;
        internal Binding(ParticleSystemRenderer source)
        {
            Source = source; Material = source.sharedMaterial; Shader = Material.shader;
            var owner = new GameObject("GloomhavenVR.Ping.Upload") { layer = source.gameObject.layer };
            owner.transform.SetParent(source.transform, false);
            owner.AddComponent<MeshFilter>().sharedMesh = Upload;
            Draw = owner.AddComponent<MeshRenderer>();
            Draw.enabled = false;
            Draw.sharedMaterial = source.sharedMaterial;
            Draw.shadowCastingMode = ShadowCastingMode.Off;
            Draw.receiveShadows = false;
            Upload.MarkDynamic();
        }
        internal void Release()
        {
            if (Draw != null) Draw.enabled = false;
            if (Claimed && Source != null) Source.forceRenderingOff = false;
            Claimed = false;
        }
        internal void Dispose()
        {
            Release();
            if (Draw != null) Destroy(Draw.gameObject);
            Destroy(Snapshot); Destroy(Upload);
        }
    }

    internal static void OnPingShown(PingManager manager)
    {
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning) return;
        try
        {
            // This is the exact assigned owner, including network-received pings.
            if (Assigned?.GetValue(manager) is not IList assigned || assigned.Count == 0) return;
            object ping = assigned[assigned.Count - 1];
            _highlight ??= ping.GetType().GetField("highlight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_highlight?.GetValue(ping) is GameObject highlight) Attach(highlight);
        }
        catch (Exception error) { Report(error); }
    }

    internal static void Attach(GameObject highlight)
    {
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || highlight == null) return;
        if (highlight.GetComponent<QuestPingStreams>() != null) return;
        var owner = highlight.AddComponent<QuestPingStreams>();
        var streams = new List<ParticleSystemVertexStream>(4);
        foreach (ParticleSystemRenderer renderer in highlight.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            Material? material = renderer.sharedMaterial;
            string shader = material != null && material.shader != null ? material.shader.name : string.Empty;
            if (renderer.renderMode != ParticleSystemRenderMode.Billboard || renderer.forceRenderingOff
                || (shader != "Particles/Priority Additive" && shader != "Particles/Additive"
                    && shader != "Legacy Shaders/Particles/Additive" && shader != "Mobile/Particles/Additive")) continue;
            renderer.GetActiveVertexStreams(streams);
            if (streams.Count != 4 || streams[0] != ParticleSystemVertexStream.Position
                || streams[1] != ParticleSystemVertexStream.Normal
                || streams[2] != ParticleSystemVertexStream.Color
                || streams[3] != ParticleSystemVertexStream.UV) continue;
            owner._bindings.Add(new Binding(renderer));
        }
        if (owner._bindings.Count == 0) { Destroy(owner); return; }
        if (Live.Count == 0) { Camera.onPreCull += BeforeCamera; Camera.onPostRender += AfterCamera; }
        Live.Add(owner);
    }

    private static void BeforeCamera(Camera camera)
    {
        if (camera != VRCameraPolicy.AllowedHead) { ReleaseAll(); return; }
        foreach (QuestPingStreams owner in Live)
        {
            if (owner == null || !owner.isActiveAndEnabled) continue;
            foreach (Binding binding in owner._bindings)
            {
                if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || binding.Source == null
                    || binding.Draw == null || !binding.Source.enabled || !binding.Source.gameObject.activeInHierarchy
                    || binding.Source.sharedMaterial != binding.Material || binding.Failed)
                { binding.Release(); continue; }
                try { Upload(binding, camera); }
                catch (Exception error) { binding.Failed = true; binding.Release(); Report(error); }
            }
        }
    }

    private static void Upload(Binding binding, Camera camera)
    {
        ParticleSystemRenderer source = binding.Source;
        source.GetActiveVertexStreams(binding.Streams);
        if (source.sharedMaterial.shader != binding.Shader || source.renderMode != ParticleSystemRenderMode.Billboard
            || binding.Streams.Count != 4 || binding.Streams[0] != ParticleSystemVertexStream.Position
            || binding.Streams[1] != ParticleSystemVertexStream.Normal || binding.Streams[2] != ParticleSystemVertexStream.Color
            || binding.Streams[3] != ParticleSystemVertexStream.UV)
        { binding.Release(); return; }
        // Honor a visibility owner which already suppressed this native draw.
        if (!binding.Claimed && source.forceRenderingOff) return;
        source.BakeMesh(binding.Snapshot, camera, true);
        binding.Draw.transform.SetPositionAndRotation(source.transform.position, Quaternion.identity);
        Vector3 scale = source.transform.lossyScale;
        if (!FiniteScale(scale.x) || !FiniteScale(scale.y) || !FiniteScale(scale.z))
            throw new InvalidOperationException("Ping owner has no finite drawable scale.");
        binding.Draw.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
        Mesh snapshot = binding.Snapshot;
        if (snapshot.vertexCount == 0) { binding.Release(); return; }
        if (snapshot.vertexCount > MaximumVertices || snapshot.subMeshCount != 1)
            throw new InvalidOperationException("Ping snapshot exceeded its bounded original billboard shape.");
        snapshot.GetVertices(binding.Positions); snapshot.GetUVs(0, binding.Uv); snapshot.GetColors(binding.Colors);
        snapshot.GetIndices(binding.Indices, 0);
        if (binding.Positions.Count != binding.Uv.Count || binding.Positions.Count != binding.Colors.Count)
            throw new InvalidOperationException("Ping snapshot has incomplete vertex inputs.");
        // Explicit Float32 COLOR removes the dynamic particle/native UNorm8
        // fetch boundary. Lists and both meshes are reused for this pooled ping.
        binding.Upload.Clear(false);
        binding.Upload.SetVertices(binding.Positions);
        binding.Upload.SetUVs(0, binding.Uv);
        binding.Upload.SetColors(binding.Colors);
        binding.Upload.SetTriangles(binding.Indices, 0, false);
        binding.Upload.bounds = snapshot.bounds;
        binding.Draw.sharedMaterial = source.sharedMaterial;
        source.GetPropertyBlock(binding.Properties); binding.Draw.SetPropertyBlock(binding.Properties);
        binding.Draw.gameObject.layer = source.gameObject.layer;
        binding.Draw.sortingLayerID = source.sortingLayerID; binding.Draw.sortingOrder = source.sortingOrder;
        source.forceRenderingOff = true; binding.Claimed = true;
        binding.Draw.enabled = true;
        if (VRLog.Wants(VRLogLevel.Debug) && _diagnostics < 4)
        {
            _diagnostics++;
            UnityEngine.Debug.Log("[Quest startup] ping-upload source=" + source.name + " vertices=" + snapshot.vertexCount
                + " stride=" + binding.Upload.GetVertexBufferStride(0) + " color=" + binding.Upload.GetVertexAttributeFormat(VertexAttribute.Color)
                + " shader=" + source.sharedMaterial.shader.name + " simulation=original camera=" + camera.name);
        }
    }

    private static void AfterCamera(Camera camera)
    {
        if (camera == VRCameraPolicy.AllowedHead) ReleaseAll();
    }
    private static void ReleaseAll() { foreach (QuestPingStreams owner in Live) if (owner != null) owner.Release(); }

    private static bool FiniteScale(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) > 0.000001f;

    private static void Report(Exception error)
    {
        if (_failureReported) return;
        _failureReported = true;
        VRLog.Warn("Board", "Quest ping upload unavailable; original ping retained (" + error.GetType().Name + ").");
    }

    internal static void ResetAll()
    {
        Camera.onPreCull -= BeforeCamera; Camera.onPostRender -= AfterCamera;
        foreach (QuestPingStreams owner in new List<QuestPingStreams>(Live))
        {
            owner.Release();
            if (owner != null) Destroy(owner);
        }
        Live.Clear();
    }

    private void Release() { foreach (Binding binding in _bindings) binding.Release(); }
    private void OnEnable() { foreach (Binding binding in _bindings) binding.Failed = false; }
    private void OnDisable() => Release();
    private void OnDestroy()
    {
        foreach (Binding binding in _bindings) binding.Dispose();
        Live.Remove(this);
        if (Live.Count == 0) { Camera.onPreCull -= BeforeCamera; Camera.onPostRender -= AfterCamera; }
    }
}
