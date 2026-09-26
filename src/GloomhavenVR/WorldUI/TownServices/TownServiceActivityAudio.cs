using System;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Quiet native foley at the resident, using the shared head listener and
/// the game's current master/effects levels. Never invokes native purchase sounds.</summary>
internal sealed class TownServiceActivityAudio : IDisposable
{
    private static readonly int SoundCount = Enum.GetValues(typeof(TownActivitySound)).Length;
    // Resident service ids are 1..3; slot zero is intentionally unused.
    private const int ResidentSlotCount = 4;
    private readonly Transform _root;
    private readonly byte _service;
    private readonly string _claim;
    private readonly TownServiceActivitySoundClock _clock = new();
    private readonly AudioSource?[] _voices = new AudioSource?[2];
    private readonly float[] _ends = new float[2], _gains = new float[2];
    private readonly AudioClip?[] _clips = new AudioClip?[SoundCount];
    private readonly float[] _resolveAt = new float[SoundCount];
    private int _voice;
    private float _voiceScale;
    private bool _failed;
    private static AudioClip? _coinClink;
    private static float _nextCoinResolve;
    private static readonly bool[] Reported = new bool[ResidentSlotCount], MissingReported = new bool[SoundCount];
    private static readonly int[] TraceLines = new int[ResidentSlotCount];
    private const int TraceLineBudgetPerResident = 8;

    internal TownServiceActivityAudio(Transform root, byte service)
    { _root = root; _service = service; _claim = "TownResidentAudio." + service; }

    internal void Tick(int author, uint epoch, float workClock, float elapsed, bool visible,
        in TownActivityVisual shown)
    {
        if (_failed) return;
        try
        {
            TownActivitySound sound = _clock.Sample(_service, author, epoch, workClock, elapsed, visible, in shown);
            // The activity clock still advances while locally muted. Its author-owned
            // phase is shared presentation state; a listener preference may silence the
            // result, but must never make this peer choose a different later sound edge.
            if (!WorldUIConfig.ImmersiveTownSoundEffects.Value) { Stop(); return; }
            if (!visible) { Stop(); return; }
            if (!HeadEar.Claim(_claim)) { _clock.Reset(); Stop(); return; }
            float now = Time.unscaledTime;
            // Resident coordinates are authored in perceived metres, while Unity's
            // AudioSource distances are world units. The map in the build 558 log
            // was 198.12 world units per metre: a fixed 4.5-unit cutoff silenced
            // every contact before it reached a visitor standing beside the counter.
            float scale = Mathf.Max(.01f, _root.lossyScale.x);
            if (scale != _voiceScale)
            {
                _voiceScale = scale;
                foreach (AudioSource? existing in _voices)
                    if (existing != null) SetHearingRange(existing, scale);
            }
            GlobalData? global = SaveData.Instance?.Global;
            float master = global == null ? 1f : Mathf.Clamp01(global.MasterVolume / 100f) * Mathf.Clamp01(global.SFXVolume / 100f);
            for (int i = 0; i < _voices.Length; i++)
            {
                AudioSource? source = _voices[i];
                if (source == null) continue;
                if (now >= _ends[i] && source.isPlaying) source.Stop();
                source.volume = master * _gains[i] * Mathf.Clamp01((_ends[i] - now) / .08f);
            }
            if (sound == TownActivitySound.None || master <= 0f) return;
            AudioClip? clip = Resolve(sound, now);
            if (clip == null) return;
            if (VRLog.WantsDebug && TraceLines[_service] < TraceLineBudgetPerResident)
            {
                TraceLines[_service]++;
                VRLog.Info("TownServices", "NPC FOLEY played service=" + _service + " event=" + sound
                    + " clip='" + clip.name + "' source=TownServiceActivitySoundClock ("
                    + TraceLines[_service] + "/" + TraceLineBudgetPerResident + ").");
            }
            int slot = _voice++ % _voices.Length;
            AudioSource voice = _voices[slot] ?? Create(slot);
            voice.Stop(); voice.clip = clip;
            voice.transform.position = _root.TransformPoint(sound == TownActivitySound.Coin ? shown.Left : shown.Right);
            voice.pitch = 1f;
            // The previous foley gain was multiplied by the game's two volume sliders
            // and then attenuated again at the visitor's normal standing distance.
            // These remain quieter than native transactions but are audible nearby.
            _gains[slot] = sound == TownActivitySound.Coin ? .065f : .12f;
            voice.volume = master * _gains[slot];
            // Native effects can contain long gameplay tails. Foley uses a bounded
            // excerpt with a short end fade; the original shared clip is untouched.
            float duration = sound == TownActivitySound.Spell ? 2.8f : sound == TownActivitySound.Coin ? .45f : .65f;
            _ends[slot] = now + Mathf.Min(duration, clip.length);
            voice.Play();
        }
        catch (Exception error)
        {
            _failed = true; Stop();
            if (!Reported[_service])
            { Reported[_service] = true; VRLog.Warn("TownServices", "NPC activity audio disabled for service " + _service + ": " + error); }
        }
    }

    private AudioSource Create(int slot)
    {
        var obj = new GameObject("Town.ActivitySound." + slot);
        obj.transform.SetParent(_root, false);
        AudioSource source = obj.AddComponent<AudioSource>();
        source.playOnAwake = false; source.loop = false; source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        SetHearingRange(source, Mathf.Max(.01f, _root.lossyScale.x));
        source.dopplerLevel = 0f; source.priority = 110;
        _voices[slot] = source; return source;
    }

    private static void SetHearingRange(AudioSource source, float worldUnitsPerMetre)
    {
        // Keep one long, linear falloff across the room. A short maxDistance
        // behaves like an audible on/off boundary when a tracked head crosses it
        // between frames, especially at the map's large world-unit scale.
        source.minDistance = .4f * worldUnitsPerMetre;
        source.maxDistance = 7f * worldUnitsPerMetre;
    }

    private AudioClip? Resolve(TownActivitySound sound, float now)
    {
        // A physically recorded-style short coin contact is bundled offline and
        // spatialized at the visible hand. The old procedural pair of high-pitched
        // oscillators sounded like an electronic chime even at reduced gain.
        if (sound == TownActivitySound.Coin)
        {
            if (_coinClink == null && now >= _nextCoinResolve)
            {
                _nextCoinResolve = now + 10f;
                _coinClink = TownServiceAssets.Audio("coin-soft");
                if (_coinClink != null && _coinClink.loadState == AudioDataLoadState.Unloaded)
                    _coinClink.LoadAudioData();
            }
            return _coinClink != null && _coinClink.loadState == AudioDataLoadState.Loaded
                ? _coinClink : null;
        }
        int index = (int)sound;
        if (_clips[index] != null) return _clips[index];
        if (now < _resolveAt[index]) return null;
        _resolveAt[index] = now + 5f;
        // The spell edge reuses the matching short clip, not the native UI/gameplay trigger.
        // Attention changes intentionally have no sound: a former fallback reused the flat
        // equipment-toggle clip and was the spatial open/close noise reported in builds 560-566.
        string id = "PlaySound_ScenarioUIAugmentLight";
        if (!AudioController.IsValidAudioID(id))
        {
            if (!MissingReported[index])
            { MissingReported[index] = true; VRLog.Warn("TownServices", "NPC native activity sound unavailable: " + id); }
            return null;
        }
        var item = AudioController.GetAudioItem(id);
        if (item?.subItems == null) return null;
        foreach (var sub in item.subItems)
            if (sub.Clip != null) { _clips[index] = sub.Clip; break; }
        return _clips[index];
    }

    private void Stop()
    {
        foreach (AudioSource? voice in _voices) if (voice != null) voice.Stop();
        HeadEar.Release(_claim);
    }
    public void Dispose()
    {
        Stop(); _clock.Reset();
        foreach (AudioSource? voice in _voices) if (voice != null) UnityEngine.Object.Destroy(voice.gameObject);
    }
}
