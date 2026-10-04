using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        // Build616 supplied numeric intermediate fades while native HIGH/toggle walls
        // still popped on Frame. Read the real renderer immediately before the head
        // camera's submission, not the scalar ledger. This diagnostic does not assert
        // visible pixels: command buffers and native shader execution follow this event.
        // Six episodes / three distinct native routes / twelve samples each per scene. Nothing
        // is sampled at normal logging, and no scene query, extra camera or readback runs.
        private const int DrawTraceEpisodes = 6;
        private const int DrawTraceRenderers = 3;
        private const int DrawTraceSamples = 12;
        private readonly List<WallDrawEpisode> _wallDrawEpisodes = new(DrawTraceEpisodes);
        private readonly List<Material> _drawMaterials = new(8);
        private readonly MaterialPropertyBlock _drawBlock = new();
        private readonly MaterialPropertyBlock _drawSlotBlock = new();
        private int _drawEpisodeCount;
        private bool _drawTraceFailed;

        private sealed class WallDrawEpisode
        {
            internal Segment Segment = null!;
            internal Renderer Renderer = null!;
            internal int Number, WriteFrame, LastDrawFrame = -1, Samples, LastBucket = -1;
            internal float ExpectedCutoff, ExpectedToggle, ExpectedOn;
            internal int ExpectedGate;
            internal Texture? ExpectedMap;
            internal bool ExpectedBlock, WasReturning, Complete;
            internal Material? WrittenMaterial;
            internal Shader? WrittenShader;
            internal bool WrittenEnabled, WrittenForceOff;
            internal string Route = "";
            internal readonly WallDrawMaterialState[] Slots = new WallDrawMaterialState[8];
            internal int WrittenSlotCount;
        }

        private struct WallDrawMaterialState
        {
            internal Material? Material;
            internal Shader? Shader;
            internal string? Keywords;
            internal float Cutoff, Toggle, On;
        }

        private void ClearWallDrawTrace()
        {
            _wallDrawEpisodes.Clear();
            _drawMaterials.Clear();
            _drawEpisodeCount = 0;
            _drawTraceFailed = false;
        }

        private void NoteWallDrawWrite(Segment seg, MeshRenderer renderer, MaterialPropertyBlock? block)
        {
            if (!VRLog.Wants(VRLogLevel.Debug) || _drawTraceFailed) return;
            try { RecordWallDrawWrite(seg, renderer, block); }
            catch (System.Exception error) { StopWallDrawTrace(error); }
        }

        private void StopWallDrawTrace(System.Exception error)
        {
            _drawTraceFailed = true;
            // A diagnostic must never break the native effect or camera continuation.
            VRLog.Debug(Name, "DRAW DELIVERY disabled for this scene after " + error.GetType().Name
                + ": " + error.Message);
        }

        private string WallDrawRoute(Renderer renderer)
        {
            _drawMaterials.Clear(); renderer.GetSharedMaterials(_drawMaterials);
            bool low = false, high = false, toggle = false;
            for (int i = 0; i < Mathf.Min(_drawMaterials.Count, 8); i++)
            {
                Material material = _drawMaterials[i];
                if (material == null || material.shader == null) continue;
                string name = material.shader.name;
                if (name.Contains("WallFade")) { if (name.Contains("Low")) low = true; else high = true; }
                else if (material.HasProperty(WallFadeOnMatId) || material.HasProperty(ToggleWallfadeMatId)) toggle = true;
            }
            return (high ? "HIGH" : "") + (low ? (high ? "+LOW" : "LOW") : "")
                + (toggle ? (high || low ? "+toggle-native" : "toggle-native") : "");
        }

        private void RecordWallDrawWrite(Segment seg, MeshRenderer renderer, MaterialPropertyBlock? block)
        {
            WallDrawEpisode? episode = null;
            int members = 0;
            bool existingSegment = false;
            foreach (WallDrawEpisode candidate in _wallDrawEpisodes)
            {
                if (candidate.Segment != seg) continue;
                existingSegment = true;
                members++;
                if (candidate.Renderer == renderer && !candidate.Complete) episode = candidate;
            }
            if (episode == null)
            {
                // Open only from the actual first visible native write. Never turn a
                // settled wall or an ownership/restoration clear into a new episode.
                if (block == null || seg.Fade <= 0f || seg.Fade >= 0.3f
                    || members >= DrawTraceRenderers || _drawEpisodeCount >= DrawTraceEpisodes && !existingSegment)
                    return;
                if (!existingSegment) _drawEpisodeCount++;
                string route = WallDrawRoute(renderer);
                // In the reported scene HIGH and toggle-native coexist in one segment.
                // Consecutive renderer IDs alone would only sample the first family.
                foreach (WallDrawEpisode candidate in _wallDrawEpisodes)
                    if (candidate.Segment == seg && candidate.Route == route) return;
                int number = _drawEpisodeCount;
                foreach (WallDrawEpisode candidate in _wallDrawEpisodes)
                    if (candidate.Segment == seg) { number = candidate.Number; break; }
                episode = new WallDrawEpisode { Segment = seg, Renderer = renderer, Number = number, Route = route };
                _wallDrawEpisodes.Add(episode);
            }
            episode.WriteFrame = Time.frameCount;
            episode.ExpectedBlock = block != null;
            episode.ExpectedCutoff = block != null ? block.GetFloat(CutoffId) : 0f;
            episode.ExpectedMap = block != null ? block.GetTexture(TilesOcclusionMapId) : null;
            episode.ExpectedGate = block != null ? block.GetInteger(ToggleWallFadeId) : 0;
            episode.ExpectedToggle = block != null ? block.GetFloat(ToggleWallfadeMatId) : 0f;
            episode.ExpectedOn = block != null ? block.GetFloat(WallFadeOnMatId) : 0f;
            episode.WrittenMaterial = renderer.sharedMaterial;
            episode.WrittenShader = episode.WrittenMaterial != null ? episode.WrittenMaterial.shader : null;
            episode.WrittenEnabled = renderer.enabled;
            episode.WrittenForceOff = renderer.forceRenderingOff;
            int bucket = Mathf.Clamp(Mathf.FloorToInt(seg.Fade * 4f), 0, 4);
            if (seg.Fade < 1f && episode.WasReturning) bucket += 5;
            if (bucket != episode.LastBucket)
            {
                _drawMaterials.Clear(); renderer.GetSharedMaterials(_drawMaterials);
                episode.WrittenSlotCount = _drawMaterials.Count;
                for (int i = 0; i < Mathf.Min(_drawMaterials.Count, 8); i++)
                {
                    Material m = _drawMaterials[i];
                    episode.Slots[i] = new WallDrawMaterialState
                    {
                        Material = m, Shader = m != null ? m.shader : null,
                        Keywords = m != null ? string.Join(",", m.shaderKeywords) : null,
                        Cutoff = m != null && m.HasProperty(CutoffId) ? m.GetFloat(CutoffId) : 0f,
                        Toggle = m != null && m.HasProperty(ToggleWallfadeMatId) ? m.GetFloat(ToggleWallfadeMatId) : 0f,
                        On = m != null && m.HasProperty(WallFadeOnMatId) ? m.GetFloat(WallFadeOnMatId) : 0f,
                    };
                }
            }
        }

        private void HandleWallDrawTrace(Camera camera)
        {
            if (!VRLog.Wants(VRLogLevel.Debug) || _drawTraceFailed || !VRSession.IsRunning
                || camera == null || camera != Rig.VRRigDriver.HeadCamera) return;
            try { SampleWallDrawTrace(camera); }
            catch (System.Exception error) { StopWallDrawTrace(error); }
        }

        private void SampleWallDrawTrace(Camera camera)
        {
            using var traceScope = PerfMonitor.Scope("WallFade.DrawTrace");
            foreach (WallDrawEpisode episode in _wallDrawEpisodes)
            {
                Renderer r = episode.Renderer;
                if (episode.Complete || r == null || episode.LastDrawFrame == Time.frameCount
                    || episode.WriteFrame != Time.frameCount) continue;
                float fade = episode.Segment.Fade;
                bool returning = fade < 1f && episode.WasReturning;
                int bucket = Mathf.Clamp(Mathf.FloorToInt(fade * 4f), 0, 4);
                if (fade >= 1f) episode.WasReturning = true;
                if (returning) bucket += 5;
                if (bucket == episode.LastBucket) continue;
                episode.LastDrawFrame = Time.frameCount;
                episode.LastBucket = bucket;
                episode.Samples++;
                r.GetPropertyBlock(_drawBlock);
                Texture? map = _drawBlock.GetTexture(TilesOcclusionMapId);
                float cutoff = _drawBlock.GetFloat(CutoffId);
                bool hasBlock = r.HasPropertyBlock();
                _drawMaterials.Clear();
                r.GetSharedMaterials(_drawMaterials);
                var slots = new System.Text.StringBuilder();
                int count = Mathf.Min(_drawMaterials.Count, 8);
                bool materialChanged = episode.WrittenSlotCount != _drawMaterials.Count;
                for (int i = 0; i < count; i++)
                {
                    Material m = _drawMaterials[i];
                    if (i > 0) slots.Append("; ");
                    if (m == null) { slots.Append(i).Append(":null"); materialChanged |= episode.Slots[i].Material != null; continue; }
                    string keywords = string.Join(",", m.shaderKeywords);
                    float nativeCutoff = m.HasProperty(CutoffId) ? m.GetFloat(CutoffId) : 0f;
                    float nativeToggle = m.HasProperty(ToggleWallfadeMatId) ? m.GetFloat(ToggleWallfadeMatId) : 0f;
                    float nativeOn = m.HasProperty(WallFadeOnMatId) ? m.GetFloat(WallFadeOnMatId) : 0f;
                    WallDrawMaterialState written = episode.Slots[i];
                    materialChanged |= m != written.Material || m.shader != written.Shader || keywords != written.Keywords
                        || nativeCutoff != written.Cutoff || nativeToggle != written.Toggle || nativeOn != written.On;
                    slots.Append(i).Append(':').Append(m.name).Append('#').Append(m.GetInstanceID())
                        .Append('/').Append(m.shader != null ? m.shader.name : "<no shader>")
                        .Append(" kw=[").Append(keywords).Append(']')
                        .Append(" cutoff=").Append(m.HasProperty(CutoffId) ? m.GetFloat(CutoffId).ToString("F4") : "n/a")
                        .Append(" toggle=").Append(m.HasProperty(ToggleWallfadeMatId) ? m.GetFloat(ToggleWallfadeMatId).ToString("F2") : "n/a")
                        .Append(" wallOn=").Append(m.HasProperty(WallFadeOnMatId) ? m.GetFloat(WallFadeOnMatId).ToString("F2") : "n/a");
                    // A material-index block overrides the whole-renderer block. Never
                    // mistake correct renderer metadata for the final native slot inputs.
                    r.GetPropertyBlock(_drawSlotBlock, i);
                    if (!_drawSlotBlock.isEmpty)
                    {
                        Texture? slotMap = _drawSlotBlock.GetTexture(TilesOcclusionMapId);
                        slots.Append(" slotBlock={cutoff=").Append(_drawSlotBlock.GetFloat(CutoffId).ToString("F4"))
                            .Append(",map=").Append(slotMap != null ? slotMap.name + "#" + slotMap.GetInstanceID() : "<none>")
                            .Append(",gateInt=").Append(_drawSlotBlock.GetInteger(ToggleWallFadeId))
                            .Append(",toggle=").Append(_drawSlotBlock.GetFloat(ToggleWallfadeMatId).ToString("F2"))
                            .Append(",wallOn=").Append(_drawSlotBlock.GetFloat(WallFadeOnMatId).ToString("F2")).Append('}');
                    }
                }
                Material? first = count > 0 ? _drawMaterials[0] : null;
                Shader? shader = first != null ? first.shader : null;
                bool changed = materialChanged || first != episode.WrittenMaterial || shader != episode.WrittenShader
                    || r.enabled != episode.WrittenEnabled || r.forceRenderingOff != episode.WrittenForceOff
                    || hasBlock != episode.ExpectedBlock || map != episode.ExpectedMap
                    || cutoff != episode.ExpectedCutoff
                    || _drawBlock.GetInteger(ToggleWallFadeId) != episode.ExpectedGate
                    || _drawBlock.GetFloat(ToggleWallfadeMatId) != episode.ExpectedToggle
                    || _drawBlock.GetFloat(WallFadeOnMatId) != episode.ExpectedOn;
                VRLog.Debug(Name, "DRAW DELIVERY: episode=" + episode.Number + "/" + DrawTraceEpisodes
                    + " sample=" + episode.Samples + "/" + DrawTraceSamples + " frame=" + Time.frameCount
                    + " eye=" + camera.stereoActiveEye + " renderer=" + r.name + "#" + r.GetInstanceID()
                    + " route=" + episode.Route + " fade=" + fade.ToString("F4") + " enabled=" + r.enabled
                    + " forceOff=" + r.forceRenderingOff + " block=" + hasBlock
                    + " cutoff=" + cutoff.ToString("F4") + " expected=" + episode.ExpectedCutoff.ToString("F4")
                    + " map=" + (map != null ? map.name + "#" + map.GetInstanceID() : "<none>")
                    + " gateInt=" + _drawBlock.GetInteger(ToggleWallFadeId)
                    + " toggle=" + _drawBlock.GetFloat(ToggleWallfadeMatId).ToString("F2")
                    + " wallOn=" + _drawBlock.GetFloat(WallFadeOnMatId).ToString("F2")
                    + " lateToDrawChanged=" + changed + " materialSlots=" + _drawMaterials.Count
                    + " [" + slots + "] named=" + count + "/" + _drawMaterials.Count
                    + "; native shader pixels remain unmeasured.");
                if (episode.Samples >= DrawTraceSamples || fade <= 0f) episode.Complete = true;
            }
        }
    }
}
