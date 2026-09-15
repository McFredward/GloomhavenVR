using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.Video;

namespace GloomhavenVR.Net;

/// <summary>Share native movie presentation without touching VideoCamera or game progression.
/// Only a native decoder may originate a movie; a cosmetic decoder cannot create a replay loop.
/// Close, decoder end/error and monotonically newer playback tokens retire old publications.</summary>
internal static class RemoteVideoPlayback
{
    private static readonly Dictionary<int, VideoSourceClock> Peers = new();
    private static VideoPlayer? _native;
    private static string _nativeClip = string.Empty;
    private static uint _token, _revision;
    private static uint _nativeGeneration;
    private static int _owner, _localId;
    private static uint _suppressedNativeToken;
    private static string _lastClip = string.Empty;
    private static float _closedUntil;
    private static uint _activeToken, _poseKey;
    private static VideoWindowState _active;
    private static float _activeAt;
    private static VideoPlayer? _mirror;
    private static uint _mirrorToken;
    private static int _mirrorOwner;
    private static uint _sentToken;
    private static bool _sentPlaying;
    private static VideoPlayer? _mutedNative;
    private static bool[]? _savedMute;
    private static AudioSource?[]? _savedAudioSources;

    internal static bool SendDue
    {
        get
        {
            TrackNative();
            bool source = _native != null && _suppressedNativeToken != _token;
            return (source ? _token : 0) != _sentToken
                || (source && _native!.isPlaying) != _sentPlaying;
        }
    }

    private static void TrackNative()
    {
        VideoPlayer? native = NativeVideoWindow.NativePlayer;
        string clip = NativeVideoWindow.PlaybackKey;
        uint generation = NativeVideoWindow.NativePlaybackGeneration;
        if (!VideoWindowCodec.ValidClip(clip)) native = null;
        if (native != null && (_native != native || _nativeClip != clip || _nativeGeneration != generation))
        {
            unchecked { _token++; }
            if (_token == 0) _token = 1;
            _closedUntil = 0;
            _suppressedNativeToken = 0;
        }
        if (_native != null && native == null)
        {
            _closedUntil = Time.unscaledTime + 3f;
            if (_owner == _localId && _activeToken == _token) FinishActivePlayback();
        }
        _native = native;
        _nativeGeneration = generation;
        _nativeClip = native != null ? clip : string.Empty;
        if (native != null) _lastClip = clip;
    }

    internal static void Sample(ref PresenceState extras, int localId)
    {
        _localId = localId;
        TrackNative();
        bool nativeSource = _native != null && _suppressedNativeToken != _token;
        _sentToken = nativeSource ? _token : 0;
        _sentPlaying = nativeSource && _native!.isPlaying;
        if (!SharedWindows.ParticipatesHere(SharedWindowKind.Video) || localId <= 0) return;
        VideoWindowState state;
        if (_native != null && _suppressedNativeToken != _token)
        {
            state = new VideoWindowState { Native = true, Playing = _native.isPlaying,
                Token = _token, Revision = ++_revision, Clip = _nativeClip,
                Millis = (uint)Math.Max(0, Math.Min(uint.MaxValue, _native.time * 1000)) };
        }
        else if (_closedUntil > Time.unscaledTime || (_native != null && _suppressedNativeToken == _token))
        {
            state = new VideoWindowState { Native = true, Closed = true,
                Token = _token, Revision = ++_revision, Clip = _lastClip };
        }
        else if (_mirror != null && _owner != 0)
        {
            state = _active;
            state.Native = false;
        }
        else return;
        // Poses name the elected playback, while native-source metadata always names THIS
        // native decoder. Two clients playing the same intro therefore do not originate loops.
        uint key = _poseKey != 0 ? _poseKey : PoseKey(localId, _token);
        state.Window = default;
        RemoteMapStory.SampleVideoPose(key, _owner == localId || _owner == 0, ref state.Window);
        extras.HasVideoWindow = true;
        extras.VideoWindow = state;
    }

    internal static void Observe(int sender, in PresenceState extras)
    {
        if (!Peers.TryGetValue(sender, out VideoSourceClock? peer)) Peers[sender] = peer = new VideoSourceClock();
        if (!extras.HasVideoWindow || !extras.VideoWindow.Native)
        {
            peer.HasSource = false;
        }
        else
        {
            if (!peer.Observe(in extras.VideoWindow, Time.unscaledTime)) return;
            if (extras.VideoWindow.Closed && sender == _owner && extras.VideoWindow.Token == _activeToken)
                FinishActivePlayback();
        }
        if (extras.HasVideoWindow)
            RemoteMapStory.ObserveVideoPose(sender, in extras.VideoWindow.Window);
        else RemoteMapStory.ForgetVideoPose(sender);
    }

    internal static void Resolve(int localId)
    {
        _localId = localId;
        TrackNative();
        if (!SharedWindows.ParticipatesHere(SharedWindowKind.Video) || localId <= 0)
        {
            StopMirror();
            _owner = 0;
            _poseKey = 0;
            NativeVideoWindow.SetNativePresentationSuppressed(false);
            RestoreNativeAudio();
            return;
        }
        bool localSource = _native != null && _suppressedNativeToken != _token;
        int owner = localSource ? localId : 0;
        VideoWindowState chosen = localSource
            ? new VideoWindowState { Native = true, Token = _token, Clip = _nativeClip } : default;
        float at = Time.unscaledTime;
        foreach (KeyValuePair<int, VideoSourceClock> kv in Peers)
        {
            VideoSourceClock peer = kv.Value;
            if (peer.HasSource && Time.unscaledTime - peer.At > 2f)
            {
                peer.HasSource = false;
            }
            if (!peer.HasSource || (owner != 0 && kv.Key >= owner)) continue;
            // The same election applies on every client, including one whose native game
            // happens to be playing a different movie. Its original callbacks keep running;
            // only the one shared presentation plane follows this room-wide authority.
            owner = kv.Key;
            chosen = peer.State;
            at = peer.At;
        }
        _owner = owner;
        _active = chosen;
        _activeAt = at;
        _activeToken = chosen.Token;
        _poseKey = owner != 0 ? PoseKey(owner, chosen.Token) : 0;
        if (owner == 0 || owner == localId) StopMirror();
        else EnsureMirror();
        bool suppressed = _native != null && _suppressedNativeToken == _token;
        NativeVideoWindow.SetNativePresentationSuppressed(suppressed);
        SyncNativeAudio(_native != null && (_mirror != null || suppressed));
        RemoteMapStory.ResolveVideoPose(_poseKey);
    }

    private static uint PoseKey(int owner, uint token)
    {
        uint key = unchecked(((uint)owner * 16777619u) ^ token);
        return key != 0 ? key : 1;
    }

    private static void EnsureMirror()
    {
        if (_mirror != null && (_mirrorOwner != _owner || _mirrorToken != _activeToken)) StopMirror();
        if (_mirror == null)
        {
            if (!NativeVideoWindow.TryResolvePlaybackKey(_active.Clip ?? string.Empty, out string url))
            {
                RetireOwner("movie asset unavailable");
                return;
            }
            var root = new GameObject("GloomhavenVR Shared Video Decoder");
            UnityEngine.Object.DontDestroyOnLoad(root);
            _mirror = root.AddComponent<VideoPlayer>();
            _mirror.playOnAwake = false;
            _mirror.isLooping = false;
            _mirror.renderMode = VideoRenderMode.APIOnly;
            _mirror.audioOutputMode = VideoAudioOutputMode.Direct;
            _mirror.url = url;
            _mirrorOwner = _owner;
            _mirrorToken = _activeToken;
            _mirror.prepareCompleted += Prepared;
            _mirror.loopPointReached += Ended;
            _mirror.errorReceived += Failed;
            NativeVideoWindow.SetRemoteSource(_mirror);
            _mirror.Prepare();
            VRLog.Info("Net", $"SHARED VIDEO START: owner={_owner}, token={_activeToken}, clip={_active.Clip}");
        }
        if (RemoteMapStory.TryVideoInitialPose(_owner, _poseKey, out Vector3 pos, out Quaternion rot, out float size))
            NativeVideoWindow.SetRemotePose(pos, rot, size);
        if (_mirror != null && _mirror.isPrepared) ApplyPlayback(_mirror, initial: false);
    }

    private static void Prepared(VideoPlayer player)
    {
        if (player != _mirror) return;
        ApplyPlayback(player, initial: true);
    }

    private static void ApplyPlayback(VideoPlayer player, bool initial)
    {
        double target = _active.Millis / 1000d
            + (_active.Playing ? Math.Max(0, Time.unscaledTime - _activeAt) : 0);
        if (player.length > 0 && target >= player.length)
        {
            RetireOwner("source reached movie end");
            return;
        }
        if (player.canSetTime && (initial || Math.Abs(player.time - target) > 0.35)) player.time = target;
        if (player.audioTrackCount > 0) player.SetDirectAudioVolume(0, AudioController.GetGlobalVolume());
        if (_active.Playing && !player.isPlaying) player.Play();
        else if (!_active.Playing && !player.isPaused) player.Pause();
    }

    private static void Ended(VideoPlayer player)
    {
        if (player == _mirror) RetireOwner("decoder reached movie end");
    }

    private static void Failed(VideoPlayer player, string reason)
    {
        if (player == _mirror) RetireOwner(reason);
    }

    private static void RetireOwner(string reason)
    {
        FinishActivePlayback();
        VRLog.Info("Net", $"SHARED VIDEO STOP: owner={_owner}, token={_activeToken}, reason={reason}");
        StopMirror();
    }

    private static void FinishActivePlayback()
    {
        // Native intros may start on several clients. Completion belongs to the elected
        // presentation source: do not hand the screen to a lagging copy of that same play.
        foreach (VideoSourceClock peer in Peers.Values)
            if (peer.HasSource && peer.State.Clip == _active.Clip) peer.Retire();
        if (_native != null && _nativeClip == _active.Clip) _suppressedNativeToken = _token;
    }

    private static void SyncNativeAudio(bool mute)
    {
        if (!mute || _mutedNative != _native) RestoreNativeAudio();
        if (!mute || _native == null || _mutedNative != null) return;
        _mutedNative = _native;
        int count = _native.audioTrackCount;
        _savedMute = new bool[count];
        _savedAudioSources = new AudioSource?[count];
        for (ushort i = 0; i < count; i++)
        {
            if (_native.audioOutputMode == VideoAudioOutputMode.Direct)
            {
                _savedMute[i] = _native.GetDirectAudioMute(i);
                _native.SetDirectAudioMute(i, true);
            }
            else if (_native.audioOutputMode == VideoAudioOutputMode.AudioSource)
            {
                AudioSource source = _native.GetTargetAudioSource(i);
                if (source == null) continue;
                bool seen = false;
                for (int j = 0; j < i; j++)
                    if (_savedAudioSources[j] == source) { seen = true; break; }
                if (seen) continue;
                _savedAudioSources[i] = source;
                _savedMute[i] = source.mute;
                source.mute = true;
            }
        }
    }

    private static void RestoreNativeAudio()
    {
        if (_mutedNative != null && _savedMute != null)
            for (ushort i = 0; i < _savedMute.Length; i++)
            {
                AudioSource? source = _savedAudioSources?[i];
                if (source != null) source.mute = _savedMute[i];
                else if (_mutedNative.audioOutputMode == VideoAudioOutputMode.Direct && i < _mutedNative.audioTrackCount)
                    _mutedNative.SetDirectAudioMute(i, _savedMute[i]);
            }
        _mutedNative = null;
        _savedMute = null;
        _savedAudioSources = null;
    }

    private static void StopMirror()
    {
        NativeVideoWindow.SetRemoteSource(null);
        if (_mirror == null) return;
        _mirror.prepareCompleted -= Prepared;
        _mirror.loopPointReached -= Ended;
        _mirror.errorReceived -= Failed;
        _mirror.Stop();
        UnityEngine.Object.Destroy(_mirror.gameObject);
        _mirror = null;
        _mirrorOwner = 0;
        _mirrorToken = 0;
    }

    internal static void Reset()
    {
        StopMirror();
        RestoreNativeAudio();
        NativeVideoWindow.SetNativePresentationSuppressed(false);
        Peers.Clear();
        _native = null;
        _nativeClip = string.Empty;
        _owner = _localId = 0;
        _suppressedNativeToken = 0;
        _lastClip = string.Empty;
        _closedUntil = 0;
        _activeToken = _poseKey = _sentToken = 0;
        _sentPlaying = false;
        RemoteMapStory.ResetVideoPose();
    }
}
