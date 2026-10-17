using System;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.Video;

namespace GloomhavenVR.WorldUI;

internal enum NativeVideoReviewStatus { Idle, Preparing, Playing, Closed, Failed, Busy }
internal enum NativeVideoReviewFailure
{
    None, InvalidContext, NativeMovieActive, RemoteMovieActive, AlreadyActive,
    MediaUnavailable, InvalidMovie, DecoderError,
}

/// <summary>
/// A deliberately invoked cosmetic ORIGINAL source, not a network mirror. It uses the
/// native movie window and existing shared clock/pose election, but never VideoCamera,
/// a native continuation, unlock process or promotion counter. No request is queued.
/// </summary>
internal static class NativeVideoReview
{
    private static VideoPlayer? _player;
    private static GameObject? _root;
    private static float _startedAt;
    private static bool _subscribed;
    private static string _lastRefusedKey = string.Empty;
    private static NativeVideoReviewFailure _lastRefusal;
    private static float _lastRefusedAt = -10f;
    private static string _selectedKey = string.Empty;
    internal static int Count => NativeVideoMovies.Count;
    internal static string GetKey(int index) => NativeVideoMovies.GetKey(index);
    private static bool OwnsDecoder => !ReferenceEquals(_player, null) || !ReferenceEquals(_root, null) || _subscribed;
    internal static bool Active => _player != null && _root != null;
    internal static NativeVideoReviewStatus Status { get; private set; }
    internal static NativeVideoReviewFailure LastFailure { get; private set; }
    internal static string LastKey { get; private set; } = string.Empty;
    internal static uint Generation { get; private set; }
    internal static VideoPlayer? Source => _player != null && _player.isPrepared
        && (_player.isPlaying || _player.isPaused) ? _player : null;
    internal static string SelectedKey => GloomhavenVR.Net.VideoMovieKeys.ReviewKey(_selectedKey);
    private static bool ContextAvailable => VRSession.IsRunning && WorldUIConfig.ConversionActive
        && VRModeStateMachine.ScenarioBoardExists && CanvasConversion.WorldCamera != null;

    internal static bool TryStart(string canonical)
    {
        if (OwnsDecoder && !Active) CloseDestroyedSource();
        if (Active) return Refuse(NativeVideoReviewFailure.AlreadyActive);
        LastKey = canonical;
        if (!ContextAvailable)
            return Refuse(NativeVideoReviewFailure.InvalidContext);
        if (NativeVideoWindow.NativePlaybackBusy) return Refuse(NativeVideoReviewFailure.NativeMovieActive);
        if (NativeVideoWindow.HasRemoteSource) return Refuse(NativeVideoReviewFailure.RemoteMovieActive);
        if (!NativeVideoMovies.IsCanonical(canonical)) return Refuse(NativeVideoReviewFailure.InvalidMovie);
        if (!NativeVideoMovies.TrySelectReview(canonical, out string selected, out string url))
            return Refuse(NativeVideoReviewFailure.MediaUnavailable);
        try
        {
            _root = new GameObject("GloomhavenVR Video Review Decoder");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.AddComponent<NativeVideoReviewLifetime>();
            _player = _root.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.isLooping = false;
            _player.renderMode = VideoRenderMode.APIOnly;
            _player.audioOutputMode = VideoAudioOutputMode.Direct;
            _player.url = url;
            _selectedKey = selected;
            _startedAt = Time.unscaledTime;
            unchecked { Generation++; }
            Status = NativeVideoReviewStatus.Preparing;
            LastFailure = NativeVideoReviewFailure.None;
            _player.prepareCompleted += Prepared;
            _player.loopPointReached += Ended;
            _player.errorReceived += Failed;
            VREvents.SceneLoaded += SceneLoaded;
            _subscribed = true;
            VRLog.Note("WorldUI", $"NATIVE VIDEO REVIEW: preparing key={selected}, opening={Generation}; cosmetic decoder has no native continuation.");
            _player.Prepare();
            return true;
        }
        catch (Exception error)
        {
            Fail(error.GetType().Name);
            return false;
        }
    }

    private static bool Refuse(NativeVideoReviewFailure reason)
    {
        LastFailure = reason;
        if (_lastRefusedKey != LastKey || _lastRefusal != reason || Time.unscaledTime - _lastRefusedAt >= 2f)
        {
            _lastRefusedKey = LastKey;
            _lastRefusal = reason;
            _lastRefusedAt = Time.unscaledTime;
            VRLog.Note("WorldUI", $"NATIVE VIDEO REVIEW: refused key={LastKey}, reason={reason}; no request queued.");
        }
        if (!Active) Status = NativeVideoReviewStatus.Busy;
        return false;
    }

    private static void Prepared(VideoPlayer player)
    {
        if (!ReferenceEquals(player, _player) || player == null) return;
        if (!ContextAvailable) { Stop(); return; }
        if (NativeVideoWindow.NativePlaybackBusy || NativeVideoWindow.HasRemoteSource) { CancelForNativePlayback(); return; }
        if (player.audioTrackCount > 0) player.SetDirectAudioVolume(0, AudioController.GetGlobalVolume());
        player.Play();
        Status = NativeVideoReviewStatus.Playing;
        LastFailure = NativeVideoReviewFailure.None;
        VRLog.Note("WorldUI", $"NATIVE VIDEO REVIEW: playing key={_selectedKey}, opening={Generation}.");
    }
    private static void Ended(VideoPlayer player) { if (ReferenceEquals(player, _player)) Stop(); }
    private static void Failed(VideoPlayer player, string reason) { if (ReferenceEquals(player, _player)) Fail(reason); }
    private static void Fail(string reason)
    {
        VRLog.Warn("WorldUI", $"NATIVE VIDEO REVIEW: failed key={_selectedKey}, opening={Generation}, reason={reason}.");
        Release();
        Status = NativeVideoReviewStatus.Failed;
        LastFailure = NativeVideoReviewFailure.DecoderError;
    }
    internal static void Tick()
    {
        if (!OwnsDecoder) return;
        if (_player == null || _root == null) { CloseDestroyedSource(); return; }
        if (!ContextAvailable)
        { Stop(); return; }
        if (NativeVideoWindow.NativePlaybackBusy || NativeVideoWindow.HasRemoteSource) { CancelForNativePlayback(); return; }
        if (Status == NativeVideoReviewStatus.Preparing && Time.unscaledTime - _startedAt > 30f)
        { Fail("decoder preparation timed out"); return; }
        if (_player.isPrepared && _player.audioTrackCount > 0)
            _player.SetDirectAudioVolume(0, AudioController.GetGlobalVolume());
    }
    // GUI intent may close the observed shared REVIEW. Lifecycle/context teardown
    // must use Stop, which owns only this machine's cosmetic source.
    internal static void RequestClose()
    {
        if (OwnsDecoder) { Stop(); return; }
        if (!GloomhavenVR.Net.RemoteVideoPlayback.RequestCloseReview(out string selected)) return;
        LastKey = NativeVideoMovies.CanonicalKey(selected);
        Status = NativeVideoReviewStatus.Closed;
        LastFailure = NativeVideoReviewFailure.None;
    }
    internal static bool TryCloseOwnedKey(string key)
    {
        if (!Active || !ContextAvailable || SelectedKey != key) return false;
        Stop();
        return true;
    }
    internal static void Stop()
    {
        if (!OwnsDecoder) return;
        VRLog.Note("WorldUI", $"NATIVE VIDEO REVIEW: closed key={_selectedKey}, opening={Generation}.");
        Release();
        Status = NativeVideoReviewStatus.Closed;
        LastFailure = NativeVideoReviewFailure.None;
    }
    internal static void CancelForNativePlayback() { if (OwnsDecoder) Stop(); }
    private static void CloseDestroyedSource()
    {
        Release();
        Status = NativeVideoReviewStatus.Closed;
        LastFailure = NativeVideoReviewFailure.None;
    }
    private static void SceneLoaded(SceneLoadedEvent _) => Stop();
    internal static void RootDestroyed(GameObject root)
    {
        if (!ReferenceEquals(root, _root)) return;
        CloseDestroyedSource();
    }
    private static void Release()
    {
        VideoPlayer? player = _player;
        GameObject? root = _root;
        _player = null;
        _root = null;
        _selectedKey = string.Empty;
        if (_subscribed) VREvents.SceneLoaded -= SceneLoaded;
        _subscribed = false;
        if (!ReferenceEquals(player, null))
        {
            player!.prepareCompleted -= Prepared;
            player.loopPointReached -= Ended;
            player.errorReceived -= Failed;
            if (player != null) player.Stop();
        }
        if (root != null) UnityEngine.Object.Destroy(root);
        NativeVideoWindow.CloseReview(player);
    }
}

internal sealed class NativeVideoReviewLifetime : MonoBehaviour
{
    private void OnDestroy() => NativeVideoReview.RootDestroyed(gameObject);
}
