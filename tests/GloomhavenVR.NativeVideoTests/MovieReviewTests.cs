using System;
using System.IO;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.Video;

internal static class MovieReviewTests
{
    internal static void Run(Action<bool, string> check)
    {
        NativeVideoWindow.Shutdown();
        VideoCamera.s_This = null;
        CanvasConversion.WorldCamera = new GameObject("Review head").AddComponent<Camera>();
        string directory = Path.Combine(Path.GetDirectoryName(typeof(NativeVideoMovies).Assembly.Location)!, "Movies");
        if (Directory.Exists(directory)) throw new Exception("Review fixture refuses existing movie directory");
        try
        {
            check(NativeVideoReview.Count == 20 && Enumerable.Range(0, NativeVideoReview.Count)
                .Select(NativeVideoReview.GetKey).Distinct().Count() == 20,
                "review catalogue covers exact twenty unique approved subjects");
            check(!Enumerable.Range(0, NativeVideoReview.Count).Select(NativeVideoReview.GetKey)
                .Any(key => key.Contains("gameplay_trailer", StringComparison.Ordinal)),
                "excluded flat gameplay trailer has no review or conversion entry");
            for (int i = 0; i < NativeVideoReview.Count; i++)
            {
                string key = NativeVideoReview.GetKey(i);
                string path = Path.Combine(directory, NativeVideoMovies.StereoKey(key));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "opaque decoder fixture " + i);
            }
            uint generation = NativeVideoReview.Generation;
            for (int i = 0; i < NativeVideoReview.Count; i++)
            {
                string key = NativeVideoReview.GetKey(i);
                check(NativeVideoReview.TryStart(key) && NativeVideoReview.Status == NativeVideoReviewStatus.Preparing
                    && NativeVideoReview.Active && !NativeVideoWindow.Visible,
                    "deliberate review prepares exactly one owned decoder without empty window: " + key);
                VideoPlayer player = CurrentDecoder();
                check(player.PrepareCalls == 1 && player.PlayCalls == 0 && !player.isLooping && !player.playOnAwake
                    && player.renderMode == VideoRenderMode.APIOnly && NativeVideoWindow.NativePlayer == null,
                    "review cannot borrow a native game lifecycle or run game callbacks: " + key);
                check(NativeVideoReview.Generation != generation, "each original review has a new source generation");
                generation = NativeVideoReview.Generation;
                player.texture = new Texture { width = 3840 }; player.frame = 0;
                player.Prepared(); NativeVideoWindow.Tick();
                check(NativeVideoWindow.Visible && NativeVideoWindow.OriginatingPlayer == player
                    && NativeVideoWindow.PlaybackKey == GloomhavenVR.Net.VideoMovieKeys.ReviewKey(NativeVideoMovies.StereoKey(key))
                    && NativeVideoReview.Status == NativeVideoReviewStatus.Playing,
                    "prepared review uses native window and explicit selected stereo identity: " + key);
                check(CanvasConversion.Ordered.Single().HostRect.sizeDelta == new Vector2(1280, 720),
                    "all review subjects retain ordinary per-eye native window geometry: " + key);
                int decoders = UnityEngine.Object.Instances.OfType<VideoPlayer>().Count();
                check(!NativeVideoReview.TryStart(NativeVideoMovies.IntroKey)
                    && NativeVideoReview.LastFailure == NativeVideoReviewFailure.AlreadyActive
                    && NativeVideoReview.LastKey == key && player.PlayCalls == 1
                    && UnityEngine.Object.Instances.OfType<VideoPlayer>().Count() == decoders,
                    "active presses report refusal without interrupting or queuing another decoder");
                check(NativeVideoWindow.TrySkipNativeMovie() && !NativeVideoReview.Active && player.StopCalls == 1
                    && !NativeVideoWindow.Visible && NativeVideoReview.Status == NativeVideoReviewStatus.Closed,
                    "review picture click releases exact cosmetic source and ordinary window");
            }
            check(NativeVideoReview.TryStart(NativeVideoMovies.IntroKey), "review can retry immediately after close");
            VideoPlayer stale = CurrentDecoder();
            Action queuedPrepared = stale.CapturePrepared();
            NativeVideoReview.Stop();
            check(NativeVideoReview.TryStart("Heroes/Summoner.mov"), "new review starts after stopping preparation");
            VideoPlayer current = CurrentDecoder();
            queuedPrepared();
            check(current.PlayCalls == 0 && stale.PlayCalls == 0 && NativeVideoReview.Status == NativeVideoReviewStatus.Preparing,
                "stale preparation callback cannot play old decoder or complete a replacement review");
            current.Failed();
            check(!NativeVideoReview.Active && current.StopCalls == 1 && NativeVideoReview.Status == NativeVideoReviewStatus.Failed
                && NativeVideoReview.LastFailure == NativeVideoReviewFailure.DecoderError,
                "decoder failure removes ownership and exposes localized reason without retry loop");
            check(NativeVideoReview.TryStart(NativeVideoMovies.IntroKey), "failed decoder can be retried deliberately");
            current = CurrentDecoder(); current.Prepared(); current.Ended();
            check(!NativeVideoReview.Active && NativeVideoReview.Status == NativeVideoReviewStatus.Closed,
                "normal review completion closes without native progression callback");
            NativeVideoReview.TryStart(NativeVideoMovies.IntroKey);
            Time.unscaledTime += 31; NativeVideoWindow.Tick();
            check(!NativeVideoReview.Active && NativeVideoReview.Status == NativeVideoReviewStatus.Failed,
                "bounded preparation timeout releases silent stuck decoder");
            VRModeStateMachine.ScenarioBoardExists = false;
            check(!NativeVideoReview.TryStart(NativeVideoMovies.IntroKey)
                && NativeVideoReview.LastFailure == NativeVideoReviewFailure.InvalidContext,
                "review is rejected outside the requested scenario context");
            VRModeStateMachine.ScenarioBoardExists = true;
            NativeVideoReview.TryStart(NativeVideoMovies.IntroKey); current = CurrentDecoder();
            VREvents.LoadScene(); current.Prepared();
            check(!NativeVideoReview.Active && current.PlayCalls == 0 && current.StopCalls == 1,
                "scene load cancels preparation before old callbacks can play");
            var nativeOwner = new GameObject("Live native owner").AddComponent<VideoCamera>();
            nativeOwner.m_Camera = nativeOwner.gameObject.AddComponent<Camera>();
            var native = nativeOwner.m_VideoPlayer = nativeOwner.gameObject.AddComponent<VideoPlayer>();
            VideoCamera.s_This = nativeOwner;
            native.isPlaying = true;
            check(!NativeVideoReview.TryStart(NativeVideoMovies.IntroKey)
                && NativeVideoReview.LastFailure == NativeVideoReviewFailure.NativeMovieActive && native.PlayCalls == 0,
                "active native movie rejects review without restarting its decoder");
            native.isPlaying = false;
            check(!NativeVideoReview.TryStart(NativeVideoMovies.IntroKey)
                && NativeVideoReview.LastFailure == NativeVideoReviewFailure.NativeMovieActive,
                "native preparation lease rejects review before first decoded frame");
            nativeOwner.m_Camera.enabled = false;
            check(NativeVideoReview.TryStart(NativeVideoMovies.IntroKey), "inactive native camera does not reserve review window");
            current = CurrentDecoder(); current.Prepared(); current.texture = new Texture { width = 3840 }; current.frame = 0;
            NativeVideoWindow.Tick();
            NativeVideoMovies.SetNativeUrl(native, "/game/Movies/Heroes/Doomstalker.mov", "Heroes/Doomstalker");
            check(!NativeVideoReview.Active && current.StopCalls == 1 && !NativeVideoWindow.Visible && native.PlayCalls == 0
                && nativeOwner.Completions == 0,
                "genuine native URL assignment cancels/reaps review without native callbacks or restart");
            check(NativeVideoReview.TryStart(NativeVideoMovies.IntroKey), "review may start before remote native takeover");
            current = CurrentDecoder(); current.Prepared(); current.texture = new Texture { width = 3840 }; current.frame = 0;
            NativeVideoWindow.Tick();
            var mirror = new GameObject("Peer review mirror").AddComponent<VideoPlayer>();
            NativeVideoWindow.SetRemoteSource(mirror);
            check(!NativeVideoReview.Active && current.StopCalls == 1 && !NativeVideoWindow.Visible
                && VREvents.SubscriberCount == 0 && !current.isPlaying,
                "elected remote takeover retires and silences review before mirror preparation");
            check(!NativeVideoReview.TryStart(NativeVideoMovies.IntroKey)
                && NativeVideoReview.LastFailure == NativeVideoReviewFailure.RemoteMovieActive,
                "pending elected peer source rejects local review instead of replacing shared presentation");
            NativeVideoWindow.SetRemoteSource(null);
            var reviewMirror = new GameObject("Shared review click").AddComponent<VideoPlayer>();
            reviewMirror.url = "/mod/Movies/Heroes/Summoner.sbs-v1.mov";
            reviewMirror.isPrepared = reviewMirror.isPlaying = true; reviewMirror.texture = new Texture { width = 3840 }; reviewMirror.frame = 0;
            NativeVideoWindow.SetRemoteSource(reviewMirror, "Review/Heroes/Summoner.sbs-v1.mov");
            NativeVideoWindow.SetRemotePose(default, default, 1f); NativeVideoWindow.Tick();
            RemoteVideoPlayback.CanCloseReview = true;
            check(NativeVideoWindow.TrySkipNativeMovie() && NativeVideoReview.Status == NativeVideoReviewStatus.Closed
                && NativeVideoReview.LastKey == "Heroes/Summoner.mov" && NativeVideoReview.LastFailure == NativeVideoReviewFailure.None,
                "peer review picture click uses explicit observer close intent with localized closed status");
            NativeVideoWindow.SetRemoteSource(reviewMirror, "Heroes/Summoner.sbs-v1.mov");
            NativeVideoWindow.SetRemotePose(default, default, 1f); NativeVideoWindow.Tick();
            int closeRequests = RemoteVideoPlayback.CloseCalls;
            check(!NativeVideoWindow.TrySkipNativeMovie() && RemoteVideoPlayback.CloseCalls == closeRequests,
                "same-picture genuine peer movie never receives observer review click or close intent");
            NativeVideoWindow.SetRemoteSource(null); RemoteVideoPlayback.CanCloseReview = false;
            NativeVideoReview.TryStart(NativeVideoMovies.IntroKey); current = CurrentDecoder();
            GameObject orphan = current.gameObject;
            Action queuedAfterDestroy = current.CapturePrepared();
            UnityEngine.Object.Destroy(current);
            NativeVideoWindow.Tick();
            check(!NativeVideoReview.Active && orphan.Destroyed && NativeVideoReview.Status == NativeVideoReviewStatus.Closed
                && VREvents.SubscriberCount == 0, "destroyed decoder component releases surviving root and scene subscription");
            check(NativeVideoReview.TryStart(NativeVideoMovies.IntroKey), "retry after destroyed component has no orphan ownership");
            VideoPlayer replacement = CurrentDecoder(); queuedAfterDestroy();
            check(replacement.PlayCalls == 0 && VREvents.SubscriberCount == 1,
                "destroyed component's queued callback cannot play replacement or duplicate scene hook");
            NativeVideoReview.Stop();
            NativeVideoReview.TryStart(NativeVideoMovies.IntroKey); current = CurrentDecoder();
            orphan = current.gameObject;
            UnityEngine.Object.Destroy(orphan);
            check(NativeVideoReview.TryStart(NativeVideoMovies.IntroKey) && VREvents.SubscriberCount == 1,
                "direct retry after externally destroyed root cleans stale identity before replacement");
            NativeVideoReview.Stop();
            NativeVideoReview.TryStart(NativeVideoMovies.IntroKey); current = CurrentDecoder();
            NativeVideoReview.TryStart(NativeVideoMovies.IntroKey);
            NativeVideoReview.RootDestroyed(current.gameObject);
            check(NativeVideoReview.LastFailure == NativeVideoReviewFailure.None, "external root closure clears previous active-press refusal");
            check(!NativeVideoReview.Active && current.StopCalls == 1, "external review root destruction retires owned decoder");
            NativeVideoReview.TryStart(NativeVideoMovies.IntroKey); current = CurrentDecoder();
            CanvasConversion.WorldCamera = null;
            current.Prepared();
            check(!NativeVideoReview.Active && current.PlayCalls == 0 && VREvents.SubscriberCount == 0,
                "camera loss during preparation cancels review before audio can start");
            CanvasConversion.WorldCamera = new GameObject("Restored review head").AddComponent<Camera>();
            NativeVideoReview.TryStart(NativeVideoMovies.IntroKey); current = CurrentDecoder(); current.Prepared();
            CanvasConversion.WorldCamera = null; NativeVideoWindow.Tick();
            check(!NativeVideoReview.Active && !current.isPlaying && current.StopCalls == 1 && VREvents.SubscriberCount == 0,
                "camera loss during playing releases hidden review and its audio");
            CanvasConversion.WorldCamera = new GameObject("Restored review head").AddComponent<Camera>();
            NativeVideoReview.TryStart(NativeVideoMovies.IntroKey); current = CurrentDecoder();
            NativeVideoWindow.Shutdown();
            check(!NativeVideoReview.Active && current.StopCalls == 1, "VR shutdown releases preparing review decoder");
            string mono = Path.Combine(directory, NativeVideoMovies.IntroKey);
            File.WriteAllText(mono, "approved mono companion");
            File.Delete(Path.Combine(directory, NativeVideoMovies.StereoIntroKey));
            check(NativeVideoReview.TryStart(NativeVideoMovies.IntroKey) && CurrentDecoder().url == mono,
                "missing review SBS retains same approved original left companion");
            NativeVideoReview.Stop(); File.Delete(mono);
            check(!NativeVideoReview.TryStart(NativeVideoMovies.IntroKey)
                && NativeVideoReview.LastFailure == NativeVideoReviewFailure.MediaUnavailable,
                "missing approved review media never invokes native game method or substitutes other movie");
        }
        finally
        {
            NativeVideoWindow.Shutdown();
            VRModeStateMachine.ScenarioBoardExists = true;
            VRSession.IsRunning = WorldUIConfig.ConversionActive = true;
            VideoCamera.s_This = null;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    private static VideoPlayer CurrentDecoder() => UnityEngine.Object.Instances.OfType<VideoPlayer>()
        .Last(player => player.gameObject.name == "GloomhavenVR Video Review Decoder");
}
