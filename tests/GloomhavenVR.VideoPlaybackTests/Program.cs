using System;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.Video;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool okay, string message)
    {
        _assertions++;
        if (!okay) throw new InvalidOperationException(message);
    }

    private static VideoPlayer Native(ushort tracks = 1)
    {
        RemoteVideoPlayback.Reset();
        SharedWindows.Online = true;
        NativeVideoWindow.AssetAvailable = true;
        NativeVideoWindow.ReviewPlayer = null;
        NativeVideoWindow.NativeKey = "Heroes/A.mov";
        NativeVideoWindow.ResolvedKey = string.Empty;
        Time.unscaledTime = 1;
        NativeVideoWindow.NativePlaybackGeneration++;
        var native = new GameObject("native").AddComponent<VideoPlayer>();
        native.isPlaying = true;
        native.isPrepared = true;
        native.audioTrackCount = tracks;
        native.audioOutputMode = VideoAudioOutputMode.Direct;
        NativeVideoWindow.NativePlayer = native;
        RemoteVideoPlayback.Resolve(2);
        return native;
    }

    private static PresenceState Publication(uint revision = 1, bool closed = false, uint token = 10)
        => new() { HasVideoWindow = true, VideoWindow = new VideoWindowState {
            Native = true, Playing = !closed, Closed = closed, Token = token,
            Revision = revision, Millis = 1000, Clip = "Heroes/A.mov" } };

    private static VideoPlayer Mirror(in PresenceState state)
    {
        RemoteVideoPlayback.Observe(1, in state);
        RemoteVideoPlayback.Resolve(2);
        Check(NativeVideoWindow.Remote != null, "Elected lower-id native source creates cosmetic decoder");
        VideoPlayer mirror = NativeVideoWindow.Remote!;
        mirror.Prepared();
        return mirror;
    }

    private static void Main()
    {
        VideoPlayer native = Native();
        PresenceState state = Publication();
        RemoteVideoPlayback.Observe(1, in state);
        RemoteVideoPlayback.Resolve(2);
        Check(!native.GetDirectAudioMute(0), "Preparing cosmetic decoder preserves native audio until ready");
        VideoPlayer mirror = Mirror(in state);
        Check(native.GetDirectAudioMute(0), "Following mirror mutes native direct audio");
        mirror.Fail();
        RemoteVideoPlayback.Resolve(2);
        Check(!NativeVideoWindow.Suppressed && native.StopCalls == 0,
            "Local cosmetic decoder error must not suppress or stop healthy native playback");
        Check(!native.GetDirectAudioMute(0), "Decoder failure restores native audio immediately");
        Check(NativeVideoWindow.Remote == null, "Failed mirror decoder is released");
        RemoteVideoPlayback.Resolve(2);
        Check(NativeVideoWindow.Remote == null, "Same failed token cannot create a per-frame decoder loop");
        state = Publication(2, token: 11);
        mirror = Mirror(in state);
        Check(mirror.isPlaying && !NativeVideoWindow.Suppressed,
            "New authoritative play can retry after a local decoder failure");

        native = Native();
        NativeVideoWindow.AssetAvailable = false;
        state = Publication();
        RemoteVideoPlayback.Observe(1, in state);
        RemoteVideoPlayback.Resolve(2);
        Check(NativeVideoWindow.Remote == null && !NativeVideoWindow.Suppressed && native.StopCalls == 0,
            "Missing local movie asset must preserve healthy original native fallback");
        NativeVideoWindow.AssetAvailable = true;
        state = Publication(2, token: 11);
        mirror = Mirror(in state);
        Check(mirror.isPlaying, "Later real play recovers after unavailable local asset");

        native = Native();
        state = Publication();
        mirror = Mirror(in state);
        int plays = mirror.PlayCalls;
        mirror.End();
        for (int i = 0; i < 5; i++) RemoteVideoPlayback.Resolve(2);
        Check(ReferenceEquals(NativeVideoWindow.Remote, mirror) && mirror.isPaused && mirror.PlayCalls == plays,
            "Cosmetic decoder end holds its last frame without replay or source retirement");
        Check(!NativeVideoWindow.Suppressed && native.StopCalls == 0,
            "Cosmetic end must not suppress healthy native authority");
        state = Publication(2, closed: true);
        RemoteVideoPlayback.Observe(1, in state);
        RemoteVideoPlayback.Resolve(2);
        Check(NativeVideoWindow.Suppressed && NativeVideoWindow.Remote == null && native.StopCalls == 0,
            "Only elected native stop retires the concurrent presentation, without touching game callbacks");
        state = Publication(3);
        RemoteVideoPlayback.Observe(1, in state);
        RemoteVideoPlayback.Resolve(2);
        Check(NativeVideoWindow.Remote == null, "Late playing snapshot cannot replay an authoritative closed token");

        native = Native(0);
        native.isPrepared = false;
        state = Publication();
        mirror = Mirror(in state);
        native.audioTrackCount = 1;
        native.isPrepared = true;
        RemoteVideoPlayback.Resolve(2);
        Check(native.GetDirectAudioMute(0),
            "Native audio tracks discovered after decoder preparation must still be muted");
        native.audioTrackCount = 2;
        native.SetDirectAudioMute(1, true);
        RemoteVideoPlayback.Resolve(2);
        Check(native.GetDirectAudioMute(0) && native.GetDirectAudioMute(1), "New track binding retains full mute coverage");
        RemoteVideoPlayback.Reset();
        Check(!native.GetDirectAudioMute(0) && native.GetDirectAudioMute(1),
            "Mute restoration preserves each original track state after count changes");

        native = Native();
        state = Publication();
        mirror = Mirror(in state);
        native.audioTrackCount = 2;
        RemoteVideoPlayback.Resolve(2);
        Check(native.GetDirectAudioMute(1), "New initially audible track is muted when native track count grows");
        RemoteVideoPlayback.Reset();
        Check(!native.GetDirectAudioMute(0) && !native.GetDirectAudioMute(1),
            "Newly discovered audible tracks restore their own original values");

        native = Native(1);
        native.audioOutputMode = VideoAudioOutputMode.AudioSource;
        state = Publication();
        mirror = Mirror(in state);
        var first = new AudioSource();
        native.Bind(0, first);
        RemoteVideoPlayback.Resolve(2);
        Check(first.mute, "AudioSource bound after preparation is muted");
        var second = new AudioSource();
        native.Bind(0, second);
        RemoteVideoPlayback.Resolve(2);
        Check(!first.mute && second.mute, "Rebinding restores old AudioSource and mutes replacement");
        native.audioOutputMode = VideoAudioOutputMode.Direct;
        RemoteVideoPlayback.Resolve(2);
        Check(!second.mute && native.GetDirectAudioMute(0), "Output mode change restores old target and mutes new direct route");
        RemoteVideoPlayback.Reset();
        Check(!native.GetDirectAudioMute(0), "Teardown restores replaced direct route");

        native = Native(2);
        native.audioOutputMode = VideoAudioOutputMode.AudioSource;
        var shared = new AudioSource();
        native.Bind(0, shared); native.Bind(1, shared);
        state = Publication();
        mirror = Mirror(in state);
        Check(shared.mute, "Duplicate native tracks share a muted target");
        RemoteVideoPlayback.Reset();
        Check(!shared.mute, "Duplicate target is restored once to its original state");
        Check(native.StopCalls == 0, "All mirror lifecycle work leaves native Stop untouched");

        native = Native();
        native.audioOutputMode = VideoAudioOutputMode.AudioSource;
        var surviving = new AudioSource();
        native.Bind(0, surviving);
        state = Publication();
        mirror = Mirror(in state);
        UnityEngine.Object.Destroy(native);
        RemoteVideoPlayback.Reset();
        Check(!surviving.mute, "Destroyed native decoder still restores surviving target AudioSources");
        StereoRevisionPlayback();
        ReviewOriginalPlayback();
        ReviewPriorityAndMirrorLifetime();
        ReviewObserverClose();
        Console.WriteLine($"Video playback production harness: {_assertions} assertions passed.");
    }

    private static void ReviewOriginalPlayback()
    {
        Native();
        NativeVideoWindow.NativePlayer = null;
        var review = new GameObject("deliberate review original").AddComponent<VideoPlayer>();
        review.isPrepared = review.isPlaying = true; review.time = 12.5;
        NativeVideoWindow.ReviewPlayer = review;
        NativeVideoWindow.ReviewKey = "Review/Ambient/Amibient_07_Dungeon_01.sbs-v1.mov";
        NativeVideoWindow.ReviewGeneration++;
        RemoteVideoPlayback.Resolve(2);
        var sampled = default(PresenceState);
        RemoteVideoPlayback.Sample(ref sampled, 2);
        Check(sampled.HasVideoWindow && sampled.VideoWindow.Native && sampled.VideoWindow.Playing
            && sampled.VideoWindow.Clip == NativeVideoWindow.ReviewKey && sampled.VideoWindow.Millis == 12500,
            "deliberate review original publishes exact selected movie and original clock through unchanged72");
        uint firstToken = sampled.VideoWindow.Token;
        NativeVideoWindow.ReviewPlayer = null;
        RemoteVideoPlayback.Resolve(2); sampled = default;
        RemoteVideoPlayback.Sample(ref sampled, 2);
        Check(sampled.HasVideoWindow && sampled.VideoWindow.Closed && sampled.VideoWindow.Token == firstToken,
            "review close publishes durable original retirement instead of leaving a remote decoder pending");
        NativeVideoWindow.ReviewPlayer = review; NativeVideoWindow.ReviewGeneration++;
        RemoteVideoPlayback.Resolve(2); sampled = default;
        RemoteVideoPlayback.Sample(ref sampled, 2);
        Check(sampled.VideoWindow.Native && !sampled.VideoWindow.Closed && sampled.VideoWindow.Token != firstToken,
            "deliberate same-movie replay has a fresh review generation and source token");
        NativeVideoWindow.ReviewPlayer = null;
        RemoteVideoPlayback.Reset();
        var owner = Publication(); owner.VideoWindow.Clip = "Review/Ambient/Amibient_07_Dungeon_01.sbs-v1.mov";
        NativeVideoWindow.ResolvedKey = "Ambient/Amibient_07_Dungeon_01.mov";
        VideoPlayer mirror = Mirror(in owner);
        Check(mirror.url == NativeVideoWindow.ResolvedKey && mirror.time == 1
            && NativeVideoWindow.RemoteKey == owner.VideoWindow.Clip,
            "remote review fallback retains exact original mono at owner clock and elected stereo provenance");
        sampled = default; RemoteVideoPlayback.Sample(ref sampled, 2);
        Check(sampled.HasVideoWindow && !sampled.VideoWindow.Native,
            "following a deliberate review remains cosmetic and cannot originate an echo source");
        RemoteVideoPlayback.Reset(); NativeVideoWindow.ResolvedKey = string.Empty;
    }

    private static void ReviewPriorityAndMirrorLifetime()
    {
        // A lower-id deliberate review must never hide a genuine game movie.
        Native(); NativeVideoWindow.NativePlayer = null;
        var review = new GameObject("low actor review").AddComponent<VideoPlayer>();
        review.isPrepared = review.isPlaying = true;
        NativeVideoWindow.ReviewPlayer = review; NativeVideoWindow.ReviewGeneration++;
        NativeVideoWindow.ReviewKey = "Review/Heroes/Summoner.sbs-v1.mov";
        RemoteVideoPlayback.Resolve(1);
        var actual = Publication(token: 51); actual.VideoWindow.Clip = "Heroes/Doomstalker.sbs-v1.mov";
        RemoteVideoPlayback.Observe(9, in actual);
        Check(NativeVideoWindow.ReviewPlayer == null && !review.isPlaying && review.StopCalls == 1,
            "fresh real native higher-id publication cancels lower-id review before election or mirror preparation");
        RemoteVideoPlayback.Resolve(1);
        Check(NativeVideoWindow.Remote != null && NativeVideoWindow.RemoteKey == actual.VideoWindow.Clip,
            "real native movie outranks lower-id review and becomes shared presentation");
        VideoPlayer mirror = NativeVideoWindow.Remote!; mirror.Prepared();
        var closed = actual; closed.VideoWindow.Closed = true; closed.VideoWindow.Playing = false; closed.VideoWindow.Revision++;
        RemoteVideoPlayback.Observe(9, in closed); RemoteVideoPlayback.Resolve(1);
        Check(NativeVideoWindow.Remote == null && NativeVideoWindow.ReviewPlayer == null,
            "native completion cannot resume cancelled review or leave hidden review audio");
        var local = new GameObject("new deliberate retry").AddComponent<VideoPlayer>(); local.isPrepared = local.isPlaying = true;
        NativeVideoWindow.ReviewPlayer = local; NativeVideoWindow.ReviewGeneration++;
        RemoteVideoPlayback.Resolve(1);
        var sample = default(PresenceState); RemoteVideoPlayback.Sample(ref sample, 1);
        Check(sample.HasVideoWindow && sample.VideoWindow.Native && !sample.VideoWindow.Closed,
            "explicit review retry remains possible after real native completion");
        // Real local sources also outrank lower-id reviews; ordinary review collisions
        // still use the existing deterministic actor-id order.
        NativeVideoWindow.ReviewPlayer = null;
        VideoPlayer native = Native();
        var otherReview = Publication(token: 61); otherReview.VideoWindow.Clip = "Review/Heroes/Summoner.sbs-v1.mov";
        RemoteVideoPlayback.Observe(1, in otherReview); RemoteVideoPlayback.Resolve(9);
        Check(NativeVideoWindow.Remote == null && native.StopCalls == 0,
            "higher-id genuine local movie outranks lower-id deliberate review without touching game playback");
        NativeVideoWindow.NativePlayer = null;
        NativeVideoWindow.ReviewPlayer = local; NativeVideoWindow.ReviewGeneration++;
        RemoteVideoPlayback.Resolve(9);
        Check(NativeVideoWindow.Remote != null && NativeVideoWindow.RemoteKey == otherReview.VideoWindow.Clip
            && NativeVideoWindow.ReviewPlayer == null,
            "two review authors deterministically elect lower actor and retire losing local decoder");
        sample = default; RemoteVideoPlayback.Sample(ref sample, 9);
        Check(!sample.VideoWindow.Native || sample.VideoWindow.Closed,
            "losing review cannot re-originate elected mirror as a source loop");
        // Destroy only the decoder COMPONENT, preserving its root. The ownership path
        // must remove that root before a new mirror replaces the component handle.
        native = Native(); actual = Publication(token: 71);
        mirror = Mirror(in actual); GameObject oldRoot = mirror.gameObject;
        UnityEngine.Object.Destroy(mirror); RemoteVideoPlayback.Resolve(2);
        VideoPlayer replacement = NativeVideoWindow.Remote!;
        Check(oldRoot.Destroyed && replacement != null && !ReferenceEquals(replacement, mirror),
            "destroyed mirror component releases surviving owned root before exact-token recovery");
        Action stalePrepared = replacement!.CapturePrepared();
        RemoteVideoPlayback.Reset(); stalePrepared();
        Check(replacement.gameObject.Destroyed && !native.GetDirectAudioMute(0) && replacement.PlayCalls == 0,
            "stale retired mirror callback cannot mute native audio or restart disposed decoder");
        native = Native(); actual = Publication(token: 81); mirror = Mirror(in actual);
        oldRoot = mirror.gameObject;
        UnityEngine.Object.Destroy(oldRoot); RemoteVideoPlayback.Resolve(2);
        Check(NativeVideoWindow.Remote != null && !ReferenceEquals(NativeVideoWindow.Remote, mirror),
            "external mirror root destruction permits exact owner/token recovery");
        RemoteVideoPlayback.Reset(); NativeVideoWindow.ReviewPlayer = null;
    }

    private static void ReviewObserverClose()
    {
        Native(); NativeVideoWindow.NativePlayer = null; RemoteVideoPlayback.Resolve(2);
        var remoteReview = Publication(token: 90);
        remoteReview.VideoWindow.Clip = "Review/Heroes/Summoner.sbs-v1.mov";
        VideoPlayer mirror = Mirror(in remoteReview);
        Check(RemoteVideoPlayback.RequestCloseReview(out string selected) && selected == remoteReview.VideoWindow.Clip
            && NativeVideoWindow.Remote == null && !mirror.isPlaying,
            "observer GUI close dismisses exact peer review immediately without native game callbacks");
        var request = default(PresenceState); RemoteVideoPlayback.Sample(ref request, 2);
        Check(request.HasVideoWindow && !request.VideoWindow.Native && request.VideoWindow.Closed && !request.VideoWindow.Playing
            && request.VideoWindow.Token == 90 && request.VideoWindow.Clip == selected && request.VideoWindow.Window.ContentKey != 0,
            "observer request carries exact review alias opening and shared owner identity on unchanged72");
        Check(!RemoteVideoPlayback.SendDue, "close retry does not preempt every frame");
        Time.unscaledTime += .11f;
        Check(RemoteVideoPlayback.SendDue, "review close retries at bounded deliberate-request cadence");
        Time.unscaledTime += 2.1f;
        RemoteVideoPlayback.Sample(ref request, 2);
        Check(!request.VideoWindow.Closed || request.VideoWindow.Native, "review close transmission stops after bounded retry window");
        remoteReview.VideoWindow.Revision++;
        RemoteVideoPlayback.Observe(1, in remoteReview); RemoteVideoPlayback.Resolve(2);
        Check(NativeVideoWindow.Remote == null, "packet loss cannot immediately reopen same dismissed review identity");
        remoteReview.VideoWindow.Token++; remoteReview.VideoWindow.Revision++;
        RemoteVideoPlayback.Observe(1, in remoteReview); RemoteVideoPlayback.Resolve(2);
        Check(NativeVideoWindow.Remote != null, "new peer review opening is distinct from dismissed old identity");
        var nativePublication = Publication(token: 100); nativePublication.VideoWindow.Clip = "Heroes/Summoner.sbs-v1.mov";
        RemoteVideoPlayback.Observe(1, in nativePublication); RemoteVideoPlayback.Resolve(2);
        Check(!RemoteVideoPlayback.RequestCloseReview(out _) && NativeVideoWindow.Remote != null,
            "observer review close refuses genuine native movie even with same underlying film");
        RemoteVideoPlayback.Reset();
        // Recreate the original source in this isolated process, then deliver the captured
        // observer request. Its owner/token key is a real receipt, never a guessed constant.
        Native(); NativeVideoWindow.NativePlayer = null;
        var owner = new GameObject("review owner").AddComponent<VideoPlayer>(); owner.isPrepared = owner.isPlaying = true;
        NativeVideoWindow.ReviewPlayer = owner; NativeVideoWindow.ReviewKey = selected; NativeVideoWindow.ReviewGeneration++;
        RemoteVideoPlayback.Resolve(1); var publication = default(PresenceState);
        RemoteVideoPlayback.Sample(ref publication, 1);
        request.VideoWindow.Native = false; request.VideoWindow.Playing = false; request.VideoWindow.Closed = true;
        request.VideoWindow.Token = publication.VideoWindow.Token; request.VideoWindow.Clip = selected;
        request.VideoWindow.Window.ContentKey = unchecked(16777619u ^ publication.VideoWindow.Token);
        NativeVideoReview.ContextAvailable = false;
        RemoteVideoPlayback.Observe(2, in request);
        Check(owner.isPlaying && NativeVideoWindow.ReviewPlayer == owner,
            "observer close cannot call an owner review after its local scenario context is lost");
        NativeVideoReview.ContextAvailable = true;
        uint realOwnerKey = request.VideoWindow.Window.ContentKey;
        uint realToken = request.VideoWindow.Token;
        request.VideoWindow.Token++;
        RemoteVideoPlayback.Observe(2, in request);
        Check(owner.isPlaying, "observer close refuses wrong opening token even with current owner pose identity");
        request.VideoWindow.Token = realToken;
        request.VideoWindow.Window.ContentKey ^= 77;
        RemoteVideoPlayback.Observe(2, in request);
        Check(owner.isPlaying, "observer close refuses a different shared owner identity");
        request.VideoWindow.Window.ContentKey = realOwnerKey;
        RemoteVideoPlayback.Observe(2, in request);
        Check(!owner.isPlaying && NativeVideoWindow.ReviewPlayer == null && owner.StopCalls == 1,
            "exact observer close reaches only currently owned cosmetic review");
        var replacement = new GameObject("replacement review owner").AddComponent<VideoPlayer>(); replacement.isPrepared = replacement.isPlaying = true;
        NativeVideoWindow.ReviewPlayer = replacement; NativeVideoWindow.ReviewGeneration++;
        RemoteVideoPlayback.Resolve(1);
        RemoteVideoPlayback.Observe(2, in request);
        Check(replacement.isPlaying && replacement.StopCalls == 0,
            "duplicate stale observer close cannot affect next review of same selected movie");
        NativeVideoWindow.ReviewPlayer = null;
        var genuine = new GameObject("genuine native replacement").AddComponent<VideoPlayer>(); genuine.isPrepared = genuine.isPlaying = true;
        NativeVideoWindow.NativePlayer = genuine; NativeVideoWindow.NativeKey = "Heroes/Summoner.sbs-v1.mov";
        RemoteVideoPlayback.Resolve(1); RemoteVideoPlayback.Observe(2, in request);
        Check(genuine.isPlaying && genuine.StopCalls == 0,
            "stale review close never forwards into genuine game decoder or continuation");
        NativeVideoReview.ContextAvailable = true; RemoteVideoPlayback.Reset();
    }

    private static void StereoRevisionPlayback()
    {
        VideoPlayer native = Native();
        NativeVideoWindow.NativeKey = "CP_Intro/GH_CP_Intro.mov";
        var state = Publication();
        state.VideoWindow.Clip = NativeVideoMovies.StereoIntroKey;
        VideoPlayer mirror = Mirror(in state);
        Check(mirror.url == NativeVideoMovies.StereoIntroKey && mirror.time == 1,
            "shared stereo revision resolves selected file at elected owner clock");
        Check(NativeVideoWindow.RemoteKey == NativeVideoMovies.StereoIntroKey,
            "cosmetic window receives elected key separately from its resolved media URL");
        var sampled = default(PresenceState);
        RemoteVideoPlayback.Sample(ref sampled, 2);
        Check(sampled.VideoWindow.Native && sampled.VideoWindow.Clip == "CP_Intro/GH_CP_Intro.mov",
            "concurrent native mono source publishes actual mono identity without local stereo upgrade");
        var closed = Publication(2, closed: true); closed.VideoWindow.Clip = NativeVideoMovies.StereoIntroKey;
        RemoteVideoPlayback.Observe(1, in closed); RemoteVideoPlayback.Resolve(2);
        Check(NativeVideoWindow.Suppressed && native.StopCalls == 0 && NativeVideoWindow.Remote == null,
            "owner stereo completion retires matching concurrent mono presentation without native callbacks");

        native = Native();
        NativeVideoWindow.ResolvedKey = "CP_Intro/GH_CP_Intro.mov";
        state = Publication(); state.VideoWindow.Clip = NativeVideoMovies.StereoIntroKey;
        int reports = GloomhavenVR.Core.VRLog.Notes.FindAll(line => line.StartsWith("SHARED VIDEO STEREO FALLBACK:", StringComparison.Ordinal)).Count;
        mirror = Mirror(in state);
        Check(mirror.url == "CP_Intro/GH_CP_Intro.mov" && mirror.time == 1 && mirror.isPlaying,
            "missing selected stereo file uses approved companion with same cosmetic owner clock");
        for (int i = 0; i < 20; i++) RemoteVideoPlayback.Resolve(2);
        Check(GloomhavenVR.Core.VRLog.Notes.FindAll(line => line.StartsWith("SHARED VIDEO STEREO FALLBACK:", StringComparison.Ordinal)).Count == reports + 1,
            "shared stereo companion fallback explicitly reports once per elected opening");
        mirror.Fail(); RemoteVideoPlayback.Resolve(2);
        Check(!native.GetDirectAudioMute(0) && native.StopCalls == 0 && !NativeVideoWindow.Suppressed,
            "failed stereo companion never stops or suppresses healthy native playback");
        native = Native(); NativeVideoWindow.AssetAvailable = false;
        state = Publication(); state.VideoWindow.Clip = NativeVideoMovies.StereoIntroKey;
        RemoteVideoPlayback.Observe(1, in state); RemoteVideoPlayback.Resolve(2);
        for (int i = 0; i < 20; i++) RemoteVideoPlayback.Resolve(2);
        Check(NativeVideoWindow.Remote == null && native.StopCalls == 0 && !NativeVideoWindow.Suppressed,
            "missing stereo and companion refuse cosmetic decoding without restart loops or native callback writes");
        native = Native(); NativeVideoWindow.NativeKey = "CP_Intro/GH_CP_Intro.mov";
        state = Publication(); state.VideoWindow.Clip = NativeVideoMovies.NativeIntroKey;
        mirror = Mirror(in state);
        Check(mirror.url == NativeVideoMovies.NativeIntroKey && mirror.time == 1
            && NativeVideoWindow.RemoteKey == NativeVideoMovies.NativeIntroKey,
            "explicit original owner elects exact original media and provenance at the same owner clock");
        sampled = default; RemoteVideoPlayback.Sample(ref sampled, 2);
        Check(sampled.VideoWindow.Clip == "CP_Intro/GH_CP_Intro.mov",
            "following an original owner does not relabel the local approved native source");
        closed = Publication(2, closed: true); closed.VideoWindow.Clip = NativeVideoMovies.NativeIntroKey;
        RemoteVideoPlayback.Observe(1, in closed); RemoteVideoPlayback.Resolve(2);
        Check(NativeVideoWindow.Suppressed && native.StopCalls == 0 && NativeVideoWindow.Remote == null,
            "original owner completion retires concurrent intro variants without native callbacks");
        native = Native(); NativeVideoWindow.NativeKey = NativeVideoMovies.NativeIntroKey;
        sampled = default; RemoteVideoPlayback.Sample(ref sampled, 2);
        Check(sampled.HasVideoWindow && sampled.VideoWindow.Native
            && sampled.VideoWindow.Clip == NativeVideoMovies.NativeIntroKey,
            "presence publishes the exact original alias without canonicalizing content selection");
        RemoteVideoPlayback.Reset();
    }
}
