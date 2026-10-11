using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Reversible render-only influence cap on owned actor/NPC renderers. The global
/// skinning cap belongs to the native game and the existing four-bone hand protection.</summary>
internal static class FigureSkinningBudget
{
    // Build612 wrote a global TwoBones cap every frame, while HandsDriver deliberately
    // raised it to FourBones to prevent torn fingers. The Frame hardware log contains
    // 12,830 repairs: this was two owners fighting, not native quality-level changes.
    // Keep AutoLod's lifecycle entry points without taking ownership of that global.
    // Individual records restore their original slots on teardown/Off/VR stop.
    internal static void Tick() { }
    internal static void Restore() { }
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
            // Auto normally follows the global setting, which hands must keep at four.
            // Give this owned body an explicit local cap instead, restoring Auto exactly
            // when disabled. A stricter native global cap still applies naturally.
            _applied = _original == SkinQuality.Auto ? (SkinQuality)limit
                : (SkinQuality)Mathf.Min((int)_original, limit);
            if (live != _applied) Renderer.quality = _applied;
        }
    }
}
