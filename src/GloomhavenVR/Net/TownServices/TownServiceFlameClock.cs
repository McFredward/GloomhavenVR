using System;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Continuous playback of the owned TownFlame shader's rate-one station clock.
/// Rendering state belongs to this renderer slot, never to its immutable shared material.</summary>
internal sealed class TownServiceFlameClock : IDisposable
{
    private static readonly int ClockId = Shader.PropertyToID("_TownAnimationTime");
    private readonly MeshRenderer _renderer;
    private readonly int _slot;
    private readonly MaterialPropertyBlock _original = new(), _block = new();
    private uint _session;
    private bool _sampled, _disposed;
    private float _sample, _clock, _ownerOffset;

    internal TownServiceFlameClock(MeshRenderer renderer, int slot)
    {
        _renderer = renderer; _slot = slot;
        renderer.GetPropertyBlock(_original, slot);
    }

    internal void Sample(uint session, float sample, float clock, float now)
    {
        if (_disposed) return;
        if (_sampled && session == _session && sample <= _sample) return;
        bool reset = !_sampled || session != _session
            || Mathf.Abs((clock - _clock) - (sample - _sample)) > .1f;
        // The fastest observed owner sample establishes the two clocks' offset. A delayed
        // or coalesced packet must not rewind a flame that is already playing between frames.
        _ownerOffset = reset ? sample - now : Mathf.Max(_ownerOffset, sample - now);
        _sampled = true; _session = session; _sample = sample; _clock = clock;
        Tick(now);
    }

    internal void Tick(float now)
    {
        if (_disposed || !_sampled || _renderer == null) return;
        float clock = _clock + Mathf.Max(0f, now + _ownerOffset - _sample);
        _renderer.GetPropertyBlock(_block, _slot);
        _block.SetFloat(ClockId, clock);
        _renderer.SetPropertyBlock(_block, _slot);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_renderer != null) _renderer.SetPropertyBlock(_original.isEmpty ? null : _original, _slot);
    }
}
