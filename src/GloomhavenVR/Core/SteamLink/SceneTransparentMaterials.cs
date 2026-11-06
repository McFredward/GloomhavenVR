using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Core.SteamLink
{
    // Head-only borrowing of known owned shader variants, not a native-material
    // coverage certificate. Scene/pool discovery is cold. A newly enrolled native
    // material is prepared before its first draw; warmed eye scopes allocate nothing.
    internal sealed class SceneTransparentMaterials : IDisposable
    {
        internal enum Refusal { None, Disarmed, Disposed, Camera, DuringRender, StackOverflow, Sequence, SlotOverflow }
        internal const int MaximumSlots = 64;
        internal const string Suffix = ".SceneCoverage";
        private static readonly int Src = Shader.PropertyToID("_SrcBlend"), Dst = Shader.PropertyToID("_DstBlend");
        private static readonly int SrcA = Shader.PropertyToID("_SceneCoverageSrcAlpha"), DstA = Shader.PropertyToID("_SceneCoverageDstAlpha");
        private static readonly int Fade = Shader.PropertyToID("_FadeAlpha"), Town = Shader.PropertyToID("_TownTransparent");
        internal const string NativeStraightAlpha = "_GHVRSceneCoverageStraightAlpha";
        internal const string NativeAdditiveAlpha = "_GHVRSceneCoverageAdditiveAlpha";
        private static readonly int NativeStraightId = Shader.PropertyToID(NativeStraightAlpha), NativeAdditiveId = Shader.PropertyToID(NativeAdditiveAlpha);
        private readonly Func<string, Shader?> _findVariant;
        private readonly Dictionary<Material, Entry> _sources = new Dictionary<Material, Entry>();
        private readonly Dictionary<Material, Entry> _clones = new Dictionary<Material, Entry>();
        private readonly Dictionary<Material, Shader> _ignored = new Dictionary<Material, Shader>();
        private readonly Dictionary<Material, Shader> _notNativeUi = new Dictionary<Material, Shader>();
        private readonly List<Entry> _retired = new List<Entry>();
        private readonly Dictionary<Material, Entry>.ValueCollection _entries;
        private readonly List<Material> _slots = new List<Material>(MaximumSlots);
        private readonly Material[][] _arrays = new Material[MaximumSlots + 1][];
        private readonly Camera?[] _stack = new Camera?[8];
        private readonly float[] _priorStraight = new float[8], _priorAdditive = new float[8];
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly HashSet<Renderer> _rendererSet = new HashSet<Renderer>();
        private readonly List<Graphic> _graphics = new List<Graphic>();
        private readonly HashSet<Graphic> _graphicSet = new HashSet<Graphic>();
        private Camera? _head;
        private int _depth;
        private bool _disposed, _faulted, _borrowed;
        private bool _globalsOwned;
        private float _savedStraight, _savedAdditive;
        internal Refusal Reason { get; private set; } = Refusal.Disarmed;
        internal int CachedMaterials => _sources.Count;
        internal int RendererCount => _renderers.Count;
        internal int SkippedMaterials { get; private set; }
        internal int UnclassifiedSlots { get; private set; }
        internal bool Ready => !_disposed && !_faulted && _head != null;

        private sealed class Entry
        {
            internal Material Source = null!, Clone = null!;
            internal Shader Original = null!;
            internal bool Dynamic, Native, Current, Seen;
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
            _head = camera; _faulted = false; ArmGlobals(); Reason = Refusal.None; return true;
        }

        // Outside all camera callbacks. Caller schedules this on scene/material
        // changes or its cold maintenance tick; never once per eye.
        internal bool RefreshCold()
        {
            if (_disposed) { Reason = Refusal.Disposed; return false; }
            if (_depth != 0) { Reason = Refusal.DuringRender; return false; }
            Restore();
            RestoreGraphics(markDirty: false);
            foreach (Entry entry in _retired)
            {
                // Retired clones retain their managed key even if Unity destroyed the object.
                _clones.Remove(entry.Clone!);
                if (entry.Clone != null) UnityEngine.Object.Destroy(entry.Clone);
            }
            _retired.Clear();
            _faulted = false; SkippedMaterials = 0; UnclassifiedSlots = 0;
            _ignored.Clear(); _notNativeUi.Clear(); ArmGlobals();
            foreach (Entry entry in _entries) entry.Seen = false;
            _renderers.Clear(); _rendererSet.Clear(); _graphics.Clear(); _graphicSet.Clear();
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
            {
                if (renderer == null) continue;
                renderer.GetSharedMaterials(_slots);
                if (_slots.Count == 0 || _slots.Count > MaximumSlots) { _slots.Clear(); continue; }
                bool found = false;
                for (int i = 0; i < _slots.Count; i++)
                {
                    Material source = Canonical(_slots[i]);
                    if (source == null || source.shader == null || !Known(source.shader.name, out bool dynamic))
                    { UnclassifiedSlots++; continue; }
                    found = true;
                    if (renderer.gameObject.activeInHierarchy) Cache(source);
                }
                if (found && _rendererSet.Add(renderer)) _renderers.Add(renderer);
                _slots.Clear();
            }
            foreach (Graphic graphic in UnityEngine.Object.FindObjectsOfType<Graphic>(true)) EnrollGraphic(graphic);
            var retired = new List<Entry>();
            foreach (Entry entry in _entries) if (!entry.Seen) retired.Add(entry);
            foreach (Entry entry in retired) Retire(entry);
            Reason = _head != null ? Refusal.None : Refusal.Disarmed;
            return true;
        }

        // Native pool/GraphicRegistry observers enrol exact arrivals, not a
        // once-per-second guess. No gameplay callback, texture or hierarchy changes.
        internal void EnrollObject(GameObject root)
        {
            if (_disposed || _head == null || root == null) return;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || _rendererSet.Contains(renderer)) continue;
                renderer.GetSharedMaterials(_slots);
                bool known = false;
                if (_slots.Count <= MaximumSlots)
                    for (int slot = 0; slot < _slots.Count && !known; slot++)
                    {
                        Material source = Canonical(_slots[slot]);
                        known = source != null && source.shader != null && Known(source.shader.name, out _);
                    }
                _slots.Clear();
                // Pooled scenery and gameplay renderers without an audited
                // shader never enter the every-eye borrowing loop. A later
                // material change remains discoverable by the cold refresh.
                if (known && _rendererSet.Add(renderer)) _renderers.Add(renderer);
            }
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) EnrollGraphic(graphic);
        }

        internal void EnrollGraphic(Graphic graphic)
        {
            if (_disposed || _head == null || graphic == null) return;
            if (_graphicSet.Add(graphic)) _graphics.Add(graphic);
            if (!graphic.gameObject.activeInHierarchy) return;
            CanvasRenderer canvas = graphic.canvasRenderer;
            BorrowGraphic(graphic, refresh: true);
        }

        internal void GraphicRebuilt(Graphic graphic)
        {
            EnrollGraphic(graphic);
            // Unity batches CanvasRenderer bindings before camera callbacks.
            // Keep only exact-native private shaders bound while this source is
            // active; their alpha-only GLOBAL state supplies each camera's mode.
            // Native/public Graphic.material and its stencil producer stay intact.
        }

        private void BorrowGraphic(Graphic graphic, bool refresh)
        {
            if (graphic == null || !graphic.isActiveAndEnabled || !graphic.gameObject.activeInHierarchy) return;
            CanvasRenderer canvas = graphic.canvasRenderer;
            if (canvas.materialCount > MaximumSlots) { Fault(Refusal.SlotOverflow); return; }
            for (int slot = 0; slot < canvas.materialCount; slot++)
            {
                Material current = canvas.GetMaterial(slot), source = Canonical(current);
                // Only the native material pipeline authorizes a new binding.
                // A direct foreign CanvasRenderer write is never ours to claim.
                if (source != graphic.materialForRendering) continue;
                Entry? entry = CacheNativeUi(source);
                if (refresh && entry != null) Prepare(entry);
                Material replacement = entry != null && entry.Current ? entry.Clone : source;
                if (current != replacement) canvas.SetMaterial(replacement, slot);
            }
        }

        private Entry? CacheNativeUi(Material source)
        {
            if (source == null) return null;
            Shader shader = source.shader;
            if (_sources.TryGetValue(source, out Entry entry) && entry.Original == shader)
            { entry.Seen = true; return entry.Native ? entry : null; }
            if (shader == null || (_notNativeUi.TryGetValue(source, out Shader prior) && prior == shader)) return null;
            if (!NativeFamily(shader.name)) { _notNativeUi[source] = shader; return null; }
            return Cache(source);
        }

        private Entry? Cache(Material source)
        {
            if (source == null) return null;
            Shader shader = source.shader;
            if (_sources.TryGetValue(source, out Entry entry))
            {
                if (entry.Original == shader) { entry.Seen = true; return entry; }
                Retire(entry); entry = null!;
            }
            if (shader == null || (_ignored.TryGetValue(source, out Shader ignored) && ignored == shader)) return null;
            if (!Known(shader.name, out bool dynamic)) { _ignored[source] = shader; return null; }
            if (entry == null)
            {
                Shader? variant = _findVariant(source.shader.name);
                if (variant == null || !variant.isSupported || variant.name != source.shader.name + Suffix)
                { SkippedMaterials++; _ignored[source] = shader; return null; }
                var clone = new Material(source) { shader = variant, hideFlags = HideFlags.HideAndDontSave };
                if (clone.passCount != source.passCount)
                { UnityEngine.Object.Destroy(clone); SkippedMaterials++; _ignored[source] = shader; return null; }
                entry = new Entry { Source = source, Original = source.shader, Clone = clone, Dynamic = dynamic, Native = NativeFamily(source.shader.name) };
                _sources.Add(source, entry); _clones.Add(clone, entry);
                Prepare(entry);
            }
            entry.Seen = true;
            return entry;
        }

        private static void Prepare(Entry entry)
        {
            entry.Current = entry.Source != null && entry.Source.shader == entry.Original && entry.Clone != null
                && (!entry.Dynamic || Alpha(entry.Source, out _, out _));
            if (!entry.Current) return;
            entry.Clone!.CopyPropertiesFromMaterial(entry.Source!);
            if (entry.Dynamic)
            {
                Alpha(entry.Source!, out int src, out int dst);
                entry.Clone.SetInt(SrcA, src); entry.Clone.SetInt(DstA, dst);
            }
        }

        // Feed ALL actual pre/post render callbacks after BindHead. A foreign
        // nested camera suspends borrowing; returning to the head reapplies it.
        internal void EnterHead(Camera camera)
        {
            if (_disposed || _faulted || _head == null) return;
            if (camera == null || _depth == _stack.Length) { Fault(Refusal.StackOverflow); return; }
            _priorStraight[_depth] = Shader.GetGlobalFloat(NativeStraightId);
            _priorAdditive[_depth] = Shader.GetGlobalFloat(NativeAdditiveId);
            _stack[_depth++] = camera;
            Shader.SetGlobalFloat(NativeStraightId, camera == _head ? 1f : 5f);
            Shader.SetGlobalFloat(NativeAdditiveId, camera == _head ? 0f : 1f);
            if (camera == _head) Apply();
            else
            {
                Restore();
                foreach (Entry entry in _entries) if (entry.Native) Prepare(entry);
            }
        }

        internal void ExitHead(Camera camera)
        {
            if (_disposed || _faulted || _head == null) return;
            if (_depth == 0 || _stack[_depth - 1] != camera) { Fault(Refusal.Sequence); return; }
            _stack[--_depth] = null;
            Shader.SetGlobalFloat(NativeStraightId, _priorStraight[_depth]);
            Shader.SetGlobalFloat(NativeAdditiveId, _priorAdditive[_depth]);
            if (_depth > 0 && _stack[_depth - 1] == _head) Apply(); else Restore();
        }

        private void Apply()
        {
            foreach (Entry entry in _entries) Prepare(entry);
            _borrowed = true;
            for (int i = 0; i < _renderers.Count && !_faulted; i++)
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
                    Entry? entry = Cache(source);
                    if (entry != null && entry.Current
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
            for (int i = 0; i < _renderers.Count; i++)
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

        private void RestoreGraphics(bool markDirty)
        {
            for (int i = 0; i < _graphics.Count; i++)
            {
                Graphic graphic = _graphics[i];
                if (graphic == null) continue;
                CanvasRenderer canvas = graphic.canvasRenderer;
                for (int slot = 0; slot < canvas.materialCount; slot++)
                {
                    Material current = canvas.GetMaterial(slot), source = Canonical(current);
                    if (current != source)
                    {
                        canvas.SetMaterial(source, slot);
                        if (markDirty) graphic.SetMaterialDirty();
                    }
                }
            }
        }

        private void Overflow(Renderer renderer)
        {
            // Public Unity GetSharedMaterials may grow its list ONCE on this
            // exceptional foreign shape change. Restore exact current slots,
            // preserve foreign writes and close this epoch until cold refresh.
            bool changed = false;
            for (int i = 0; i < _slots.Count; i++) changed |= Canonical(_slots[i]) != _slots[i];
            if (changed)
            {
                Material[] current = _slots.ToArray();
                for (int i = 0; i < current.Length; i++) current[i] = Canonical(current[i]);
                renderer.sharedMaterials = current;
            }
            _slots.Clear(); _faulted = true; Reason = Refusal.SlotOverflow;
        }

        private Material Canonical(Material material)
            // Unity's oblivious Material[] slots legitimately include null;
            // preserve that slot instead of inventing a fallback material.
            => material != null && _clones.TryGetValue(material, out Entry entry) ? entry.Source : material!;

        private void Retire(Entry entry)
        {
            _sources.Remove(entry.Source); entry.Current = false;
            // A persistent native UI batch can still hold this exact clone.
            // Retain its canonical alias until the next cold restore sweep.
            _retired.Add(entry);
        }

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
            if (NativeFamily(name)) return true;
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

        private static bool NativeFamily(string name) => name == "GUI/AbilityCard_Shd" || name == "GUI/AbilityCard_Add_Shd"
            || name == "VFX/GUI_Smoke_Shd" || name == "VFX/ParticleMasterUnlitBlend_Shd"
            || name == "VFX/ParticleFlow_Blend_Shd" || name == "SimpleParticleAlphaDFade" || name == "ParticleTrailShd";

        private void ArmGlobals()
        {
            if (_globalsOwned || _head == null) return;
            _savedStraight = Shader.GetGlobalFloat(NativeStraightId);
            _savedAdditive = Shader.GetGlobalFloat(NativeAdditiveId);
            _globalsOwned = true;
            Shader.SetGlobalFloat(NativeStraightId, 5f);
            Shader.SetGlobalFloat(NativeAdditiveId, 1f);
        }

        private void RestoreGlobals()
        {
            if (!_globalsOwned) return;
            Shader.SetGlobalFloat(NativeStraightId, _savedStraight);
            Shader.SetGlobalFloat(NativeAdditiveId, _savedAdditive);
            _globalsOwned = false;
        }

        private void Fault(Refusal reason)
        {
            Restore(); _faulted = true; Reason = reason;
            RestoreGraphics(markDirty: true); RestoreGlobals();
            for (int i = 0; i < _stack.Length; i++) _stack[i] = null;
            _depth = 0;
        }

        internal void DisarmImmediate()
        {
            Restore();
            RestoreGraphics(markDirty: true); RestoreGlobals();
            for (int i = 0; i < _stack.Length; i++) _stack[i] = null;
            _depth = 0; _head = null; Reason = _disposed ? Refusal.Disposed : Refusal.Disarmed;
        }

        public void Dispose()
        {
            if (_disposed) return;
            DisarmImmediate(); _disposed = true; Reason = Refusal.Disposed;
            foreach (Entry entry in _entries) if (entry.Clone != null) UnityEngine.Object.Destroy(entry.Clone);
            foreach (Entry entry in _retired) if (entry.Clone != null) UnityEngine.Object.Destroy(entry.Clone);
            _sources.Clear(); _clones.Clear(); _ignored.Clear(); _renderers.Clear(); _rendererSet.Clear();
            _graphics.Clear(); _graphicSet.Clear();
            _notNativeUi.Clear(); _retired.Clear();
        }
    }
}
