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
        Console.WriteLine($"Video playback production harness: {_assertions} assertions passed.");
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
