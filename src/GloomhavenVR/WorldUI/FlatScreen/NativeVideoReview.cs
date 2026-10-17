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
    private static string _selectedKey = string.Empty;
    internal static int Count => NativeVideoMovies.Count;
    internal static string GetKey(int index) => NativeVideoMovies.GetKey(index);
    internal static bool Active => _player != null;
    internal static NativeVideoReviewStatus Status { get; private set; }
    internal static NativeVideoReviewFailure LastFailure { get; private set; }
    internal static string LastKey { get; private set; } = string.Empty;
    internal static uint Generation { get; private set; }
    internal static VideoPlayer? Source => _player != null && _player.isPrepared
        && (_player.isPlaying || _player.isPaused) ? _player : null;
    internal static string SelectedKey => _selectedKey;

    internal static bool TryStart(string canonical)
    {
        if (Active) return Refuse(NativeVideoReviewFailure.AlreadyActive);
        LastKey = canonical;
        if (!VRSession.IsRunning || !WorldUIConfig.ConversionActive
            || !VRModeStateMachine.ScenarioBoardExists || CanvasConversion.WorldCamera == null)
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
        if (!Active) Status = NativeVideoReviewStatus.Busy;
        return false;
    }

    private static void Prepared(VideoPlayer player)
    {
        if (player != _player) return;
        if (NativeVideoWindow.NativePlaybackBusy) { CancelForNativePlayback(); return; }
        if (player.audioTrackCount > 0) player.SetDirectAudioVolume(0, AudioController.GetGlobalVolume());
        player.Play();
        Status = NativeVideoReviewStatus.Playing;
        LastFailure = NativeVideoReviewFailure.None;
        VRLog.Note("WorldUI", $"NATIVE VIDEO REVIEW: playing key={_selectedKey}, opening={Generation}.");
    }
    private static void Ended(VideoPlayer player) { if (player == _player) Stop(); }
    private static void Failed(VideoPlayer player, string reason) { if (player == _player) Fail(reason); }
    private static void Fail(string reason)
    {
        VRLog.Warn("WorldUI", $"NATIVE VIDEO REVIEW: failed key={_selectedKey}, opening={Generation}, reason={reason}.");
        Release();
        Status = NativeVideoReviewStatus.Failed;
        LastFailure = NativeVideoReviewFailure.DecoderError;
    }
    internal static void Tick()
    {
        if (_player == null) return;
        if (!VRSession.IsRunning || !WorldUIConfig.ConversionActive || !VRModeStateMachine.ScenarioBoardExists)
        { Stop(); return; }
        if (NativeVideoWindow.NativePlaybackBusy) { CancelForNativePlayback(); return; }
        if (Status == NativeVideoReviewStatus.Preparing && Time.unscaledTime - _startedAt > 30f)
        { Fail("decoder preparation timed out"); return; }
        if (_player.isPrepared && _player.audioTrackCount > 0)
            _player.SetDirectAudioVolume(0, AudioController.GetGlobalVolume());
    }
    internal static void Stop()
    {
        if (!Active) return;
        VRLog.Note("WorldUI", $"NATIVE VIDEO REVIEW: closed key={_selectedKey}, opening={Generation}.");
        Release();
        Status = NativeVideoReviewStatus.Closed;
        LastFailure = NativeVideoReviewFailure.None;
    }
    internal static void CancelForNativePlayback() { if (Active) Stop(); }
    private static void SceneLoaded(SceneLoadedEvent _) => Stop();
    internal static void RootDestroyed(GameObject root)
    {
        if (!ReferenceEquals(root, _root)) return;
        Release();
        Status = NativeVideoReviewStatus.Closed;
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
        if (player != null)
        {
            player.prepareCompleted -= Prepared;
            player.loopPointReached -= Ended;
            player.errorReceived -= Failed;
            player.Stop();
        }
        if (root != null) UnityEngine.Object.Destroy(root);
        NativeVideoWindow.CloseReview(player);
    }
}

internal sealed class NativeVideoReviewLifetime : MonoBehaviour
{
    private void OnDestroy() => NativeVideoReview.RootDestroyed(gameObject);
}
