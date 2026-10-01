using System;
using System.Collections.Generic;
using ClockStone;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI;

internal enum TownVoiceReaction : byte
{
    MerchantOffer, MerchantBuy, MerchantSell, PriestessDonate, EnchantressEnhance,
    PriestessUnavailable, EnchantressOffer, EnchantressInspect,
    MerchantUnaffordable, MerchantSoldOut
}

/// <summary>Original resident lines, baked offline with one consistent voice per
/// resident. The elected face author starts each cue; TLV80 carries its cue, generation
/// and age so every observer hears and articulates the same shared performance.
/// Speech never opens, closes or continues a native gameplay dialog.</summary>
internal static class TownServiceVoice
{
    private static readonly string[] Ears = { "TownResidents.1", "TownResidents.2", "TownResidents.3" };
    // Each event owns five complete, separately recorded performances. The elected
    // face author chooses one member of the event pool; the exact cue then travels
    // in TLV80, so a random choice can never make peers hear or articulate different
    // lines. Keep these ranges aligned with TownServiceVoiceSchedule's first-cue
    // constants and the offline exporter.
    private static readonly string[] Names = {
        "merchant-greet", "merchant-greet-2", "merchant-greet-3", "merchant-greet-4", "merchant-greet-5",
        "priestess-greet", "priestess-greet-2", "priestess-greet-3", "priestess-greet-4", "priestess-greet-5",
        "enchantress-greet", "enchantress-greet-2", "enchantress-greet-3", "enchantress-greet-4", "enchantress-greet-5",
        "merchant-offer", "merchant-offer-2", "merchant-offer-3", "merchant-offer-4", "merchant-offer-5",
        "merchant-buy", "merchant-buy-2", "merchant-buy-3", "merchant-buy-4", "merchant-buy-5",
        "merchant-sell", "merchant-sell-2", "merchant-sell-3", "merchant-sell-4", "merchant-sell-5",
        "priestess-prayer", "priestess-prayer-2", "priestess-prayer-3", "priestess-prayer-4", "priestess-prayer-5",
        "priestess-donate", "priestess-donate-2", "priestess-donate-3", "priestess-donate-4", "priestess-donate-5",
        "enchantress-cast-ember", "enchantress-cast-echo", "enchantress-cast-spark",
        "enchantress-cast-veil", "enchantress-cast-rune",
        "enchantress-enhance", "enchantress-enhance-2", "enchantress-enhance-3",
        "enchantress-enhance-4", "enchantress-enhance-5",
        "enchantress-invite", "enchantress-invite-2", "enchantress-invite-3",
        "enchantress-invite-4", "enchantress-invite-5",
        "priestess-unavailable", "priestess-unavailable-2", "priestess-unavailable-3",
        "priestess-unavailable-4", "priestess-unavailable-5",
        "enchantress-inspect", "enchantress-inspect-2", "enchantress-inspect-3",
        "enchantress-inspect-4", "enchantress-inspect-5",
        "merchant-unaffordable", "merchant-unaffordable-2", "merchant-unaffordable-3",
        "merchant-unaffordable-4", "merchant-unaffordable-5",
        "merchant-sold-out", "merchant-sold-out-2", "merchant-sold-out-3",
        "merchant-sold-out-4", "merchant-sold-out-5"
    };
    private static readonly AudioClip?[] Clips = new AudioClip?[Names.Length];
    private static readonly TownServiceVoiceCurve?[] Curves = new TownServiceVoiceCurve?[Names.Length];
    private struct RelayStamp { internal uint Session, Sequence; }
    private static readonly Dictionary<ulong, RelayStamp> Relayed = new();
    /// <summary>Owner-to-face-author presentation request. Integration sends only a cue kind,
    /// source session and monotonic event sequence; no item/card identity or native command.</summary>
    internal static Action<byte, TownVoiceReaction>? RelayRequest;
    internal static Func<TownVoiceReaction, bool>? StockRelayRequest;
    private static bool _missingRelayReported;
    private static TownServiceVoiceSchedule _schedule = new();
    private sealed class Playback
    {
        internal AudioSource? Source;
        internal ushort Cue;
        internal uint Generation;
        internal uint Epoch;
        internal int Author;
        internal float AuthoredAge;
        internal float LastSeek;
    }
    private static readonly Playback[] Playbacks = { new(), new(), new() };
    private static float _nextContext;
    private static bool _bound, _probed, _narration, _disabled;
    private static float _volume;
    private static int _frame = -1;

    internal static byte ServiceForCue(ushort cue) => cue >= 1 && cue <= 5 || cue >= 16 && cue <= 30
        || cue >= 66 && cue <= 75 ? (byte)1
        : cue >= 6 && cue <= 10 || cue >= 31 && cue <= 40 || cue >= 56 && cue <= 60 ? (byte)2
        : cue >= 11 && cue <= 15 || cue >= 41 && cue <= 55 || cue >= 61 && cue <= 65 ? (byte)3 : (byte)0;

    internal static ushort GreetingFirstCue(byte service) => service == 1 ? (ushort)1
        : service == 2 ? (ushort)6 : service == 3 ? (ushort)11 : (ushort)0;

    internal static bool IsPrayerCue(ushort cue) => cue >= 31 && cue <= 35;
    internal static bool IsWhisperedCastCue(ushort cue) => cue >= 41 && cue <= 45;

    internal static void Tick(byte service, float workClock, bool visible, in TownActivityVisual shown,
        bool lookingAtVisitor)
    {
        Ensure();
        if (StoryComposite.PointOfNoReturn) { SilenceForStory(); return; }
        if (service < 1 || service > 3) return;
        if (TownServicePopulation.IsFaceAuthor)
        {
            bool wasPending = service == 1 && _schedule.At(service).Pending;
            _schedule.Work(service, workClock, shown.Cast, shown.Attention, lookingAtVisitor,
                visible, Time.unscaledTime);
            if (service == 1 && !wasPending && _schedule.At(service).Pending
                && _schedule.At(service).PendingCue >= 1 && _schedule.At(service).PendingCue <= 5
                && VRLog.WantsDebug)
                VRLog.Debug("TownServices", "Merchant gaze queued speech: bodyAttention="
                    + shown.Attention.ToString("F2") + ", visible=" + visible);
        }
        else
            _schedule.FollowerAttention(service, shown.Attention, visible);
    }

    /// <summary>Called for a real native interaction: an opened merchant offer, a
    /// confirmed trade, donation or enhancement. Never on speculative hover.
    /// A non-author cannot invent a divergent shared cue.</summary>
    internal static void RequestReaction(byte service, TownVoiceReaction reaction)
    {
        if (StoryComposite.PointOfNoReturn) return;
        ushort firstCue = ReactionFirstCue(service, reaction);
        if (firstCue == 0) return;
        if (!TownServicePopulation.IsFaceAuthor)
        {
            if (RelayRequest != null) RelayRequest(service, reaction);
            else if (!_missingRelayReported)
            {
                _missingRelayReported = true;
                VRLog.Warn("TownServices", "Resident voice reaction relay is unavailable; remote visitor speech will be skipped.");
            }
            return;
        }
        Ensure();
        _schedule.Request(service, firstCue, Time.unscaledTime);
    }

    internal static bool IsStockReaction(TownVoiceReaction reaction) => reaction == TownVoiceReaction.MerchantOffer
        || reaction == TownVoiceReaction.MerchantUnaffordable || reaction == TownVoiceReaction.MerchantSoldOut;

    /// <summary>Physical stock inspection is cosmetic and independent of private fan
    /// focus or payment. Only the resident author chooses and times the shared take.</summary>
    internal static bool RequestStockReaction(TownVoiceReaction reaction)
    {
        if (StoryComposite.PointOfNoReturn || !IsStockReaction(reaction)) return false;
        if (!TownServicePopulation.IsFaceAuthor) return StockRelayRequest?.Invoke(reaction) == true;
        Ensure();
        _schedule.Request(1, ReactionFirstCue(1, reaction), Time.unscaledTime);
        return true;
    }

    /// <summary>Accept only a fresh, ordered presentation event on the elected author.
    /// The transport separately validates its owner/session envelope; this second guard
    /// prevents duplicate town snapshots or late delivery from replaying a reaction.</summary>
    internal static bool AcceptRelayedReaction(byte service, TownVoiceReaction reaction,
        int sourcePeer, uint sourceSession, uint sequence, float ageSeconds)
        => AcceptReaction(service, reaction, sourcePeer, sourceSession, sequence, ageSeconds, stock: false);

    internal static bool AcceptRelayedStockReaction(TownVoiceReaction reaction,
        int sourcePeer, uint sourceSession, uint sequence, float ageSeconds)
        => IsStockReaction(reaction)
            && AcceptReaction(1, reaction, sourcePeer, sourceSession, sequence, ageSeconds, stock: true);

    private static bool AcceptReaction(byte service, TownVoiceReaction reaction,
        int sourcePeer, uint sourceSession, uint sequence, float ageSeconds, bool stock)
    {
        ushort firstCue = ReactionFirstCue(service, reaction);
        if (StoryComposite.PointOfNoReturn || !TownServicePopulation.IsFaceAuthor || firstCue == 0 || sourcePeer <= 0
            || sourceSession == 0 || sequence == 0 || !TownServiceVoiceSchedule.Finite(ageSeconds)
            || ageSeconds < 0f || ageSeconds > 3f) return false;
        // Private and lifted-stock generations are independent lifetimes. Sharing
        // their replay key could silently discard a valid overlapping pickup.
        ulong key = ((ulong)(uint)sourcePeer << 8) | (stock ? (byte)4 : service);
        if (Relayed.TryGetValue(key, out RelayStamp last)
            && (sourceSession == last.Session && unchecked((int)(sequence - last.Sequence)) <= 0
                || sourceSession != last.Session && unchecked((int)(sourceSession - last.Session)) <= 0)) return false;
        // Multiplayer has at most four visitors. A wildly growing table indicates
        // malformed identity churn; bound it without touching native gameplay.
        if (Relayed.Count >= 16 && !Relayed.ContainsKey(key)) Relayed.Clear();
        Relayed[key] = new RelayStamp { Session = sourceSession, Sequence = sequence };
        Ensure();
        _schedule.Request(service, firstCue, Time.unscaledTime);
        return true;
    }

    private static ushort ReactionFirstCue(byte service, TownVoiceReaction reaction) => reaction switch
        {
            TownVoiceReaction.MerchantOffer when service == 1 => 16,
            TownVoiceReaction.MerchantBuy when service == 1 => 21,
            TownVoiceReaction.MerchantSell when service == 1 => 26,
            TownVoiceReaction.MerchantUnaffordable when service == 1 => 66,
            TownVoiceReaction.MerchantSoldOut when service == 1 => 71,
            TownVoiceReaction.PriestessDonate when service == 2 => 36,
            TownVoiceReaction.EnchantressEnhance when service == 3 => 46,
            TownVoiceReaction.EnchantressOffer when service == 3 => 51,
            TownVoiceReaction.PriestessUnavailable when service == 2 => 56,
            TownVoiceReaction.EnchantressInspect when service == 3 => 61,
            _ => 0
        };

    private static void Ensure()
    {
        if (!_bound)
        {
            _bound = true;
            TownServiceFaceSpeech.Sampler = Sample;
            TownServiceFaceSpeech.Curve = Curve;
            TownServiceFaceSpeech.Observer = Observe;
            TownServiceFaceSpeech.ResetObserver = Reset;
        }
        if (!_probed)
        {
            _probed = true;
            int unavailable = 0;
            var examples = new string[3];
            for (int i = 0; i < Names.Length; i++)
            {
                try
                {
                    Clips[i] = TownServiceAssets.Audio(Names[i]);
                    TextAsset? text = TownServiceAssets.Text(Names[i]);
                    if (text != null) Curves[i] = TownServiceVoiceCurve.Parse(text.text, (ushort)(i + 1));
                    AudioClip? clip = Clips[i];
                    if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                    if (clip == null || Curves[i] == null || Mathf.Abs(clip.length - Curves[i]!.Duration) > .06f)
                    {
                        Clips[i] = null; Curves[i] = null;
                        if (unavailable < examples.Length) examples[unavailable] = Names[i];
                        unavailable++;
                    }
                }
                catch (Exception ex)
                {
                    if (unavailable < examples.Length) examples[unavailable] = Names[i] + " (" + ex.GetType().Name + ")";
                    unavailable++;
                }
            }
            if (unavailable > 0)
                VRLog.Warn("TownServices", "Resident speech unavailable for " + unavailable + "/" + Names.Length
                    + " cues (first: " + string.Join(", ", examples, 0, Math.Min(unavailable, examples.Length))
                    + "); native services remain usable.");
        }
        Refresh();
    }

    private static void Refresh()
    {
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;
        float now = Time.unscaledTime;
        if (StoryComposite.PointOfNoReturn)
        {
            SilenceForStory();
            _narration = false; _disabled = true; _volume = 0f;
            return;
        }
        for (byte service = 1; service <= 3; service++)
        {
            bool visiting = TownServicePresentation.Active && TownServicePresentation.Service == service;
            float age = visiting ? TownServicePresentation.SessionAge : float.PositiveInfinity;
            foreach (TownServiceSessionInfo remote in TownServiceMirror.RemoteSessions.Values)
            {
                if (!remote.Active || remote.Service != service || now - remote.ReceivedTime > NetProtocol.StaleTimeoutSeconds) continue;
                visiting = true; age = Mathf.Min(age, remote.SessionAge + Mathf.Max(0f, now - remote.ReceivedTime));
            }
            // A follower must not accumulate private visit greetings which could
            // suddenly become audible when authority changes. It observes the
            // elected author's exact cue instead of scheduling from its own UI.
            if (TownServicePopulation.IsFaceAuthor) _schedule.Visit(service, visiting, age, now);
        }
        if (now < _nextContext) return;
        _nextContext = now + .1f;
        _narration = false; _disabled = false; _volume = 0f;
        try
        {
            AudioController? controller = AudioController.DoesInstanceExist();
            if (controller == null) { _disabled = true; return; }
            _disabled = controller.DisableAudio;
            GlobalData? settings = SaveData.Instance?.Global;
            _volume = settings != null ? Mathf.Clamp01(settings.MasterVolume / 100f) * Mathf.Clamp01(settings.StoryVolume / 100f) : 0f;
            foreach (AudioObject playing in AudioController.GetPlayingAudioObjects())
            {
                if (playing == null) continue;
                for (AudioCategory? category = playing.category; category != null; category = category.parentCategory)
                    if (category.Name != null && category.Name.StartsWith("VONarration", StringComparison.Ordinal))
                    { _narration = true; break; }
                if (_narration) break;
            }
        }
        catch (Exception)
        { _disabled = true; } // No audio controller during scene teardown is not a gameplay fault.
    }

    private static bool Sample(byte service, out ushort cue, out uint generation, out float age, out Vector3 mouth)
    {
        cue = 0; generation = 0; age = 0f; mouth = Vector3.zero;
        if (service < 1 || service > 3) return false;
        Ensure();
        if (StoryComposite.PointOfNoReturn) { SilenceForStory(); return false; }
        TownServiceVoiceSchedule.Entry entry = _schedule.At(service);
        // Only the author gates new speech on bundled clip readiness. Once chosen,
        // observer face curves remain independent of local volume and narration.
        uint previousGeneration = entry.Generation;
        _schedule.Sample(service, Duration, Time.unscaledTime, _narration || _disabled);
        if (service == 1 && entry.Generation != previousGeneration && VRLog.WantsDebug)
            VRLog.Debug("TownServices", "Merchant speech started: cue=" + entry.Cue
                + ", narration=" + _narration + ", audioDisabled=" + _disabled);
        cue = entry.Cue; generation = entry.Generation;
        if (cue == 0) return false;
        age = Mathf.Max(0f, Time.unscaledTime - entry.Started);
        mouth = Curve(service, cue, age);
        return true;
    }

    private static float Duration(ushort cue) => cue >= 1 && cue <= Names.Length &&
        Curves[cue - 1] != null && Clips[cue - 1] != null && Clips[cue - 1]!.loadState == AudioDataLoadState.Loaded
            ? Curves[cue - 1]!.Duration : 0f;

    private static bool Valid(byte service, ushort cue) => cue >= 1 && cue <= Names.Length
        && ServiceForCue(cue) == service && Curves[cue - 1] != null;

    private static Vector3 Curve(byte service, ushort cue, float age) => Valid(service, cue)
        ? Curves[cue - 1]!.At(age) : Vector3.zero;

    private static void Observe(byte service, int author, ushort cue, uint generation, float age, Transform head)
    {
        if (service < 1 || service > 3 || head == null || cue != 0 && !Valid(service, cue)) return;
        if (StoryComposite.PointOfNoReturn) { SilenceForStory(); return; }
        Refresh();
        uint epoch = TownServicePopulation.PerformanceEpoch;
        if (!_schedule.Observe(service, author, cue, generation, age, Time.unscaledTime, epoch)) return;
        Playback playback = Playbacks[service - 1];
        string ear = Ears[service - 1];
        if (cue == 0)
        {
            if (playback.Cue != 0 && playback.Source != null) playback.Source.Stop();
            playback.Cue = 0; playback.Generation = 0; HeadEar.Release(ear);
            return;
        }
        AudioClip? clip = Clips[cue - 1];
        if (clip == null || age >= clip.length || clip.loadState != AudioDataLoadState.Loaded)
        {
            // A late join can receive an already-finished authored cue. Do not
            // leave an older utterance audible while the shared mouth is silent.
            if (playback.Cue != 0 && playback.Source != null) playback.Source.Stop();
            playback.Cue = 0; playback.Generation = 0; HeadEar.Release(ear);
            return;
        }
        if (!HeadEar.Claim(ear)) return;
        if (playback.Source == null)
        {
            var host = new GameObject("GloomhavenVR.TownResident.Voice");
            playback.Source = host.AddComponent<AudioSource>();
            playback.Source.playOnAwake = false; playback.Source.loop = false; playback.Source.ignoreListenerPause = true; playback.Source.spatialBlend = 1f;
            playback.Source.dopplerLevel = 0f; playback.Source.spread = 0f; playback.Source.rolloffMode = AudioRolloffMode.Linear;
        }
        float scale = TownServicePopulation.Frame != null ? Mathf.Abs(TownServicePopulation.Frame.lossyScale.x) : 1f;
        AudioSource source = playback.Source;
        source.transform.position = head.position;
        // The source remains continuously audible across the whole approach to a
        // stand. The previous 4.2 m edge was easy to cross between two headset
        // samples and sounded like a switch even with Unity's linear rolloff.
        source.minDistance = Mathf.Max(.01f, .45f * scale);
        source.maxDistance = Mathf.Max(.02f, 7f * scale);
        // The final priestess performances measure -26.84 LUFS on average, 2.24 LU below the
        // merchant set and with a softer spectral balance. Compensate at the source rather than
        // rewriting/limiting the WAVs: dynamics and shared cue timing stay intact. Prayer keeps
        // the same relative murmur-to-speech ratio.
        float speechGain = IsPrayerCue(cue) ? .12f : IsWhisperedCastCue(cue) ? .22f : .42f;
        if (service == 2) speechGain *= 1.30f;
        source.volume = _disabled || _narration || !WorldUIConfig.ImmersiveTownSpeech.Value
            ? 0f : _volume * speechGain;
        bool lifetimeChanged = playback.Author != author || playback.Epoch != epoch;
        bool different = playback.Cue != cue || playback.Generation != generation
            || lifetimeChanged && age + .001f < playback.AuthoredAge;
        if (different)
        {
            source.Stop(); source.clip = clip;
            source.time = Mathf.Clamp(age, 0f, Mathf.Max(0f, clip.length - .001f));
            source.Play();
            playback.Cue = cue; playback.Generation = generation;
            playback.LastSeek = Time.unscaledTime;
        }
        else if (source.isPlaying && Time.unscaledTime - playback.LastSeek >= .5f && Mathf.Abs(source.time - age) > .15f)
        {
            source.time = Mathf.Clamp(age, 0f, Mathf.Max(0f, clip.length - .001f));
            playback.LastSeek = Time.unscaledTime;
        }
        // Authority handover may adopt the same still-playing take. Keep it
        // continuous; a reset event age in a new lifetime is a new performance.
        playback.Author = author; playback.Epoch = epoch; playback.AuthoredAge = age;
    }

    internal static void Reset()
    {
        for (int i = 0; i < Playbacks.Length; i++)
        {
            Playback playback = Playbacks[i];
            if (playback.Source != null) { playback.Source.Stop(); Object.Destroy(playback.Source.gameObject); playback.Source = null; }
            playback.Cue = 0; playback.Generation = 0; playback.Epoch = 0; playback.Author = 0; playback.AuthoredAge = 0f; playback.LastSeek = 0f;
            HeadEar.Release(Ears[i]);
        }
        _schedule = new TownServiceVoiceSchedule();
        _nextContext = 0f; _frame = -1;
        // Bundled immutable clips/curves survive scene changes. Bundle teardown itself
        // invalidates Unity objects; Ensure detects that on the next map lifecycle.
        _probed = false;
        Relayed.Clear();
        Array.Clear(Clips, 0, Clips.Length); Array.Clear(Curves, 0, Curves.Length);
    }

    private static void SilenceForStory()
    {
        _schedule.Silence(Time.unscaledTime);
        for (int i = 0; i < Playbacks.Length; i++)
        {
            Playback playback = Playbacks[i];
            if (playback.Source != null && playback.Source.isPlaying) playback.Source.Stop();
            playback.Cue = 0; playback.Generation = 0;
            HeadEar.Release(Ears[i]);
        }
    }
}
