using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Reversible render-only influence cap. Explicit native FourBones renderers bypass
/// QualitySettings.skinWeights, so original renderer slots require their own ownership.</summary>
internal static class FigureSkinningBudget
{
    private static SkinWeights _original, _applied;
    private static bool _owned;
    internal static void Tick()
    {
        int limit = VRSession.IsRunning ? PerfConfig.MaximumSkinningBones : 0;
        if (limit == 0) { Restore(); return; }
        SkinWeights live = QualitySettings.skinWeights;
        if (_owned && live != _applied) { _owned = false; } // native quality-level change
        if (!_owned) { _original = live; _owned = true; }
        _applied = (SkinWeights)Mathf.Min((int)_original, limit);
        if (live != _applied) QualitySettings.skinWeights = _applied;
    }
    internal static void Restore()
    {
        if (_owned && QualitySettings.skinWeights == _applied) QualitySettings.skinWeights = _original;
        _owned = false;
    }
    internal sealed class Record
    {
        internal SkinnedMeshRenderer Renderer = null!;
        private SkinQuality _original, _applied;
        private bool _owned;
        internal void Apply(bool restore = false)
        {
            if (Renderer == null) { _owned = false; return; }
            int limit = !restore && VRSession.IsRunning ? PerfConfig.MaximumSkinningBones : 0;
            SkinQuality live = Renderer.quality;
            if (_owned && live != _applied) _owned = false;
            if (limit == 0)
            {
                if (_owned && live == _applied) Renderer.quality = _original;
                _owned = false; return;
            }
            if (!_owned) { _original = live; _owned = true; }
            // Auto remains Auto and follows the owned global budget; explicit quality is capped.
            _applied = _original == SkinQuality.Auto ? SkinQuality.Auto
                : (SkinQuality)Mathf.Min((int)_original, limit);
            if (live != _applied) Renderer.quality = _applied;
        }
    }
}
