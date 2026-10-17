using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core.SteamLink
{
    // Head-only borrowing of known owned shader variants, not a native-material
    // coverage certificate. All scanning and material construction is cold.
    internal sealed class SceneTransparentMaterials : IDisposable
    {
        internal enum Refusal { None, Disarmed, Disposed, Camera, DuringRender, StackOverflow, Sequence, SlotOverflow }
        internal const int MaximumSlots = 64;
        internal const string Suffix = ".SceneCoverage";
        private static readonly int Src = Shader.PropertyToID("_SrcBlend"), Dst = Shader.PropertyToID("_DstBlend");
        private static readonly int SrcA = Shader.PropertyToID("_SceneCoverageSrcAlpha"), DstA = Shader.PropertyToID("_SceneCoverageDstAlpha");
        private static readonly int Fade = Shader.PropertyToID("_FadeAlpha"), Town = Shader.PropertyToID("_TownTransparent");
        private readonly Func<string, Shader?> _findVariant;
        private readonly Dictionary<Material, Entry> _sources = new Dictionary<Material, Entry>();
        private readonly Dictionary<Material, Entry> _clones = new Dictionary<Material, Entry>();
        private readonly Dictionary<Material, Entry>.ValueCollection _entries;
        private readonly List<Material> _slots = new List<Material>(MaximumSlots);
        private readonly Material[][] _arrays = new Material[MaximumSlots + 1][];
        private readonly Camera?[] _stack = new Camera?[8];
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private Renderer[] _renderers = new Renderer[0];
        private Camera? _head;
        private int _depth;
        private bool _disposed, _faulted, _borrowed;
        internal Refusal Reason { get; private set; } = Refusal.Disarmed;
        internal int CachedMaterials => _sources.Count;
        internal int RendererCount => _renderers.Length;
        internal int SkippedMaterials { get; private set; }
        internal int UnclassifiedSlots { get; private set; }
        internal bool Ready => !_disposed && !_faulted && _head != null;

        private sealed class Entry
        {
            internal Material Source = null!, Clone = null!;
            internal Shader Original = null!;
            internal bool Dynamic, Current;
        }

        internal SceneTransparentMaterials(Func<string, Shader?> findVariant)
        {
            _findVariant = findVariant ?? throw new ArgumentNullException(nameof(findVariant));
            _entries = _sources.Values;
            // Setter copies these arrays into the Renderer; reuse is safe after
            // each synchronous assignment. No ToArray occurs on an admitted path.
            for (int i = 0; i <= MaximumSlots; i++) _arrays[i] = new Material[i];
        }

        internal bool BindHead(Camera camera)
        {
            DisarmImmediate();
            if (_disposed) { Reason = Refusal.Disposed; return false; }
            if (camera == null) { Reason = Refusal.Camera; return false; }
            _head = camera; _faulted = false; Reason = Refusal.None; return true;
        }

        // Outside all camera callbacks. Caller schedules this on scene/material
        // changes or its cold maintenance tick; never once per eye.
        internal bool RefreshCold()
        {
            if (_disposed) { Reason = Refusal.Disposed; return false; }
            if (_depth != 0) { Reason = Refusal.DuringRender; return false; }
            Restore();
            _faulted = false; SkippedMaterials = 0; UnclassifiedSlots = 0;
            var admitted = new List<Renderer>();
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (renderer == null || !renderer.gameObject.activeInHierarchy) continue;
                renderer.GetSharedMaterials(_slots);
                if (_slots.Count == 0 || _slots.Count > MaximumSlots) { _slots.Clear(); continue; }
                bool found = false;
                for (int i = 0; i < _slots.Count; i++)
                {
                    Material source = Canonical(_slots[i]);
                    if (source == null || source.shader == null || !Known(source.shader.name, out bool dynamic))
                    { UnclassifiedSlots++; continue; }
                    if (!_sources.TryGetValue(source, out Entry entry))
                    {
                        Shader? variant = _findVariant(source.shader.name);
                        if (variant == null || !variant.isSupported || variant.name != source.shader.name + Suffix)
                        { SkippedMaterials++; continue; }
                        var clone = new Material(source) { shader = variant, hideFlags = HideFlags.HideAndDontSave };
                        if (clone.passCount != source.passCount)
                        { UnityEngine.Object.Destroy(clone); SkippedMaterials++; continue; }
                        entry = new Entry { Source = source, Original = source.shader, Clone = clone, Dynamic = dynamic };
                        _sources.Add(source, entry); _clones.Add(clone, entry);
                    }
                    found = true;
                }
                if (found) admitted.Add(renderer);
                _slots.Clear();
            }
            _renderers = admitted.ToArray(); Reason = _head != null ? Refusal.None : Refusal.Disarmed;
            return true;
        }

        // Feed ALL actual pre/post render callbacks after BindHead. A foreign
        // nested camera suspends borrowing; returning to the head reapplies it.
        internal void EnterHead(Camera camera)
        {
            if (_disposed || _faulted || _head == null) return;
            if (camera == null || _depth == _stack.Length) { Fault(Refusal.StackOverflow); return; }
            _stack[_depth++] = camera;
            if (camera == _head) Apply(); else Restore();
        }

        internal void ExitHead(Camera camera)
        {
            if (_disposed || _faulted || _head == null) return;
            if (_depth == 0 || _stack[_depth - 1] != camera) { Fault(Refusal.Sequence); return; }
            _stack[--_depth] = null;
            if (_depth > 0 && _stack[_depth - 1] == _head) Apply(); else Restore();
        }

        private void Apply()
        {
            foreach (Entry entry in _entries)
            {
                entry.Current = entry.Source != null && entry.Source.shader == entry.Original && entry.Clone != null
                    && (!entry.Dynamic || Alpha(entry.Source, out _, out _));
                if (!entry.Current) continue;
                entry.Clone.CopyPropertiesFromMaterial(entry.Source);
                if (entry.Dynamic)
                {
                    Alpha(entry.Source, out int src, out int dst);
                    entry.Clone.SetInt(SrcA, src); entry.Clone.SetInt(DstA, dst);
                }
            }
            _borrowed = true;
            for (int i = 0; i < _renderers.Length && !_faulted; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                renderer.GetSharedMaterials(_slots);
                if (_slots.Count > MaximumSlots) { Overflow(renderer); Restore(); return; }
                bool rendererState = StateBlock(renderer, -1), changed = false;
                Material[] next = _arrays[_slots.Count];
                for (int slot = 0; slot < _slots.Count; slot++)
                {
                    Material current = _slots[slot], source = Canonical(current), replacement = source;
                    if (source != null && _sources.TryGetValue(source, out Entry entry) && entry.Current
                        && !rendererState && !StateBlock(renderer, slot)) replacement = entry.Clone;
                    next[slot] = replacement;
                    changed |= current != replacement;
                }
                if (changed) renderer.sharedMaterials = next;
                _slots.Clear();
            }
        }

        private bool StateBlock(Renderer renderer, int slot)
        {
            if (!renderer.HasPropertyBlock()) return false;
            if (slot < 0) renderer.GetPropertyBlock(_block); else renderer.GetPropertyBlock(_block, slot);
            return _block.HasProperty(Src) || _block.HasProperty(Dst) || _block.HasProperty(SrcA) || _block.HasProperty(DstA);
        }

        private void Restore()
        {
            if (!_borrowed) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null) continue;
                renderer.GetSharedMaterials(_slots);
                if (_slots.Count > MaximumSlots) { Overflow(renderer); continue; }
                Material[] next = _arrays[_slots.Count]; bool changed = false;
                for (int slot = 0; slot < _slots.Count; slot++)
                {
                    Material current = _slots[slot]; next[slot] = Canonical(current);
                    changed |= current != next[slot];
                }
                // Restore only our still-present clones. New foreign slots or
                // another owner's replacement always win over a stale snapshot.
                if (changed) renderer.sharedMaterials = next;
                _slots.Clear();
            }
            _borrowed = false;
        }

        private void Overflow(Renderer renderer)
        {
            // Public Unity GetSharedMaterials may grow its list ONCE on this
            // exceptional foreign shape change. Restore exact current slots,
            // preserve foreign writes and close this epoch until cold refresh.
            Material[] current = _slots.ToArray(); bool changed = false;
            for (int i = 0; i < current.Length; i++)
            { Material source = Canonical(current[i]); changed |= source != current[i]; current[i] = source; }
            if (changed) renderer.sharedMaterials = current;
            _slots.Clear(); _faulted = true; Reason = Refusal.SlotOverflow;
        }

        private Material Canonical(Material material)
            => material != null && _clones.TryGetValue(material, out Entry entry) ? entry.Source : material;

        // Only the audited dynamic source properties, never stale native bag
        // fields. Unknown/feedback blend modes remain the original material.
        private static bool Alpha(Material source, out int srcAlpha, out int dstAlpha)
        {
            srcAlpha = 0; dstAlpha = 1;
            if (!source.HasProperty(Src) || !source.HasProperty(Dst)) return false;
            float src = source.GetFloat(Src), dst = source.GetFloat(Dst);
            if ((src == 5 || src == 1) && dst == 10) { srcAlpha = 1; dstAlpha = 10; return true; }
            if ((src == 5 || src == 1) && dst == 1) return true;
            // These exact owned outputs emit A1 in their ordinary opaque mode.
            // Keep this prefix state unchanged; no late opaque coverage promise.
            if (src == 1 && dst == 0 && source.renderQueue <= 2500
                && ((source.HasProperty(Fade) && source.GetFloat(Fade) == 1)
                    || (source.HasProperty(Town) && source.GetFloat(Town) <= .5f)))
            { srcAlpha = 1; dstAlpha = 0; return true; }
            return false;
        }

        private static bool Known(string name, out bool dynamic)
        {
            dynamic = false;
            switch (name)
            {
                case "GloomhavenVR/EnvParticleAlpha": case "GloomhavenVR/ActorBarSprite":
                case "GloomhavenVR/HexDecalStable": case "GloomhavenVR/EnvParticleAdd":
                case "GloomhavenVR/EnvGlow": case "GloomhavenVR/EnvFlame": case "GloomhavenVR/EnvDrip":
                case "GloomhavenVR/EnvBeam": case "GloomhavenVR/EnvShaft": case "GloomhavenVR/EnvStarPoints":
                case "GloomhavenVR/EnvPuddle": return true;
                case "GloomhavenVR/Overlay": case "GloomhavenVR/BoardLit":
                case "GloomhavenVR/TownNpc": case "GloomhavenVR/WaterVR": dynamic = true; return true;
                default: return false;
            }
        }

        private void Fault(Refusal reason)
        {
            Restore(); _faulted = true; Reason = reason;
            for (int i = 0; i < _stack.Length; i++) _stack[i] = null;
            _depth = 0;
        }

        internal void DisarmImmediate()
        {
            Restore();
            for (int i = 0; i < _stack.Length; i++) _stack[i] = null;
            _depth = 0; _head = null; Reason = _disposed ? Refusal.Disposed : Refusal.Disarmed;
        }

        public void Dispose()
        {
            if (_disposed) return;
            DisarmImmediate(); _disposed = true; Reason = Refusal.Disposed;
            foreach (Entry entry in _entries) if (entry.Clone != null) UnityEngine.Object.Destroy(entry.Clone);
            _sources.Clear(); _clones.Clear(); _renderers = new Renderer[0];
        }
    }
}
