using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board.Patches;

/// <summary>
/// Retain the original native ping draw and its animation. B712 replaced both
/// renderers with BakeMesh uploads and forceRenderingOff during head culling.
/// The exact original systems author cullingMode=Pause: the native autoplay
/// witness reproduces their clocks freezing at0.10s/0.02s while the unchanged
/// original advances beyond1s. A visible MeshRenderer proxy does not make its
/// source ParticleSystem visible. Manual Simulate(.5) masked this regression.
/// Restore the previously qualified native POSITION/COLOR/UV stream request;
/// never suppress/bake either renderer or mutate native simulation/culling.
/// The separate Adreno tile artifact remains a hardware issue, not a reason to
/// sacrifice the working original effect or change an unrelated shader.
/// </summary>
internal sealed class QuestPingStreams : MonoBehaviour
{
    private static readonly FieldInfo? Assigned = typeof(PingManager).GetField("assigned", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static FieldInfo? _highlight;
    private static readonly List<QuestPingStreams> Live = new();
    private static readonly List<ParticleSystemVertexStream> Packed = new()
        { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV };
    private readonly List<Binding> _bindings = new(2);
    private readonly List<ParticleSystemVertexStream> _scratch = new(4);
    private static bool _failureReported;

    private sealed class Binding
    {
        internal readonly ParticleSystemRenderer Renderer;
        internal readonly Material Material;
        internal readonly Shader Shader;
        internal readonly List<ParticleSystemVertexStream> Original;
        internal Binding(ParticleSystemRenderer renderer, List<ParticleSystemVertexStream> original)
        { Renderer = renderer; Material = renderer.sharedMaterial; Shader = Material.shader; Original = original; }
    }

    internal static void OnPingShown(PingManager manager)
    {
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning) return;
        try
        {
            // The postfix follows assigned.Add(ping), including network-received
            // pings. Read that exact original owner; no scene census/name guess.
            if (Assigned?.GetValue(manager) is not IList assigned || assigned.Count == 0) return;
            object ping = assigned[assigned.Count - 1];
            _highlight ??= ping.GetType().GetField("highlight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_highlight?.GetValue(ping) is GameObject highlight) Attach(highlight);
        }
        catch (Exception error)
        {
            if (_failureReported) return;
            _failureReported = true;
            // Preserve the B712 diagnostic grep token across the rollback.
            VRLog.Warn("Board", "Quest ping upload unavailable; original ping retained (" + error.GetType().Name + ").");
        }
    }

    internal static void Attach(GameObject highlight)
    {
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || highlight == null) return;
        QuestPingStreams? owner = highlight.GetComponent<QuestPingStreams>();
        if (owner != null) return;
        owner = highlight.AddComponent<QuestPingStreams>();
        foreach (ParticleSystemRenderer renderer in highlight.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            Material? material = renderer.sharedMaterial;
            string shader = material != null && material.shader != null ? material.shader.name : string.Empty;
            if (renderer.renderMode != ParticleSystemRenderMode.Billboard || renderer.forceRenderingOff
                || (shader != "Particles/Priority Additive" && shader != "Particles/Additive"
                    && shader != "Legacy Shaders/Particles/Additive" && shader != "Mobile/Particles/Additive")) continue;
            var streams = new List<ParticleSystemVertexStream>(4);
            renderer.GetActiveVertexStreams(streams);
            if (streams.Count != 4 || streams[0] != ParticleSystemVertexStream.Position
                || streams[1] != ParticleSystemVertexStream.Normal
                || streams[2] != ParticleSystemVertexStream.Color
                || streams[3] != ParticleSystemVertexStream.UV) continue;
            owner._bindings.Add(new Binding(renderer, streams));
        }
        if (owner._bindings.Count == 0) { Destroy(owner); return; }
        Live.Add(owner);
        owner.Apply();
    }

    private void Apply()
    {
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning) return;
        foreach (Binding binding in _bindings)
        {
            if (binding.Renderer == null || binding.Material == null || binding.Renderer.sharedMaterial != binding.Material
                || binding.Material.shader != binding.Shader || binding.Renderer.renderMode != ParticleSystemRenderMode.Billboard) continue;
            binding.Renderer.GetActiveVertexStreams(_scratch);
            if (Equal(_scratch, binding.Original)) binding.Renderer.SetActiveVertexStreams(Packed);
        }
    }

    private void Restore()
    {
        foreach (Binding binding in _bindings)
        {
            if (binding.Renderer == null) continue;
            binding.Renderer.GetActiveVertexStreams(_scratch);
            if (Equal(_scratch, Packed)) binding.Renderer.SetActiveVertexStreams(binding.Original);
        }
    }

    private static bool Equal(List<ParticleSystemVertexStream> left, List<ParticleSystemVertexStream> right)
    {
        if (left.Count != right.Count) return false;
        for (int index = 0; index < left.Count; index++) if (left[index] != right[index]) return false;
        return true;
    }

    internal static void ResetAll()
    {
        foreach (QuestPingStreams owner in new List<QuestPingStreams>(Live))
        {
            owner.Restore();
            if (owner != null) Destroy(owner);
        }
        Live.Clear();
    }

    private void OnEnable() => Apply();
    private void OnDisable() => Restore();
    private void OnDestroy() { Restore(); Live.Remove(this); }
}
