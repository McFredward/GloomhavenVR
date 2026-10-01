using System;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>One spatial physical mechanism performance per shared cabinet turn.
/// The already-synchronized rack epoch selects the edge; listener preferences stay local.</summary>
internal sealed class TownServiceCabinetAudio : IDisposable
{
    private readonly Transform _anchor;
    private AudioSource? _source;
    private AudioClip? _clip;
    private uint _epoch;
    private float _resolveAt;
    private bool _missingReported;

    internal TownServiceCabinetAudio(Transform anchor) => _anchor = anchor;

    internal void Begin(uint epoch, float elapsed)
    {
        if (epoch == 0 || epoch == _epoch) return;
        _epoch = epoch;
        // A late observer adopts the visible mechanism phase without replaying a
        // start that happened before it arrived. Ordinary transport cadence lands
        // comfortably inside this small opening window.
        if (elapsed > .2f || !WorldUIConfig.ImmersiveTownSoundEffects.Value) return;
        float now = Time.unscaledTime;
        if (_clip == null && now >= _resolveAt)
        {
            _resolveAt = now + 5f;
            _clip = TownServiceAssets.Audio("cabinet-cycle");
            if (_clip != null && _clip.loadState == AudioDataLoadState.Unloaded) _clip.LoadAudioData();
        }
        if (_clip == null || _clip.loadState != AudioDataLoadState.Loaded)
        {
            if (!_missingReported)
            {
                _missingReported = true;
                VRLog.Warn("TownServices", "Merchant cabinet foley unavailable; the physical controls remain usable.");
            }
            return;
        }
        AudioSource source = _source ??= Create();
        GlobalData? global = SaveData.Instance?.Global;
        float volume = global == null ? 1f
            : Mathf.Clamp01(global.MasterVolume / 100f) * Mathf.Clamp01(global.SFXVolume / 100f);
        source.Stop(); source.clip = _clip;
        source.time = Mathf.Clamp(elapsed, 0f, Mathf.Max(0f, _clip.length - .001f));
        source.volume = volume * .55f;
        source.Play();
    }

    internal void Tick()
    {
        if (_source == null) return;
        // The foley source is deliberately outside the captured cabinet hierarchy.
        // Keep its acoustic origin at the mechanism when the shared map frame moves.
        _source.transform.position = _anchor.position;
        if (!WorldUIConfig.ImmersiveTownSoundEffects.Value)
        { _source.Stop(); return; }
        GlobalData? global = SaveData.Instance?.Global;
        float volume = global == null ? 1f
            : Mathf.Clamp01(global.MasterVolume / 100f) * Mathf.Clamp01(global.SFXVolume / 100f);
        _source.volume = volume * .55f;
        SetRange(_source);
    }

    private AudioSource Create()
    {
        var host = new GameObject("Town.MerchantCabinet.Foley");
        // Build 601's first category press appended this child below HousingRoot,
        // after the immutable 34-node rack template had been registered. Its path
        // hashes to 8D884334, exactly the extra sender binding in the hardware log.
        // The resulting 35-node rack was rejected on every observer, hiding their
        // stock and leaving the dependency clock (and public buttons) waiting forever.
        // Audio is driven by the synchronized rack epoch; its playback-only component
        // must not become another original visual node. A sibling follows the same
        // shared frame without changing the source or observer template topology.
        host.transform.SetParent(_anchor.parent, false);
        host.transform.position = _anchor.position;
        AudioSource source = host.AddComponent<AudioSource>();
        source.playOnAwake = false; source.loop = false; source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear; source.dopplerLevel = 0f;
        source.priority = 112; SetRange(source); return source;
    }

    private void SetRange(AudioSource source)
    {
        float scale = Mathf.Max(.01f, Mathf.Abs(_anchor.lossyScale.x));
        source.minDistance = .25f * scale;
        source.maxDistance = 5f * scale;
    }

    public void Dispose()
    {
        if (_source != null) UnityEngine.Object.Destroy(_source.gameObject);
        _source = null; _clip = null;
    }
}
