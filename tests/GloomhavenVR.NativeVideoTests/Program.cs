using System;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

static class Program
{
    private static int _assertions;
    private static void Check(bool condition, string reason)
    {
        _assertions++;
        if (!condition) throw new Exception("Native video assertion: " + reason);
    }

    static void Main()
    {
        CanvasConversion.WorldCamera = new GameObject("Head").AddComponent<Camera>();
        var owner = new GameObject("Native").AddComponent<VideoCamera>();
        owner.m_Camera = owner.gameObject.AddComponent<Camera>();
        var native = owner.m_VideoPlayer = owner.gameObject.AddComponent<VideoPlayer>();
        VideoCamera.s_This = owner;
        native.url = @"D:\game\StreamingAssets\Movies\CP_Intro\GH_CP_Intro.mov";
        native.isPlaying = true;
        NativeVideoWindow.Tick();
        Check(!NativeVideoWindow.Visible, "unprepared movie creates no empty window");
        native.isPrepared = true;
        native.texture = new Texture();
        NativeVideoWindow.Tick();
        Check(!NativeVideoWindow.Visible, "first decoded frame gates the grab bar");
        native.frame = 0;
        NativeVideoWindow.Tick();
        Check(NativeVideoWindow.Visible, "native movie is visible");
        Check(NativeVideoWindow.PlaybackKey == "CP_Intro/GH_CP_Intro.mov", "wire identity has no machine path");
        var window = NativeVideoWindow.Window!;
        Check(!window.enabled, "identity cannot register native window lifecycle");
        Check(NativeVideoWindow.TryGetGrab(out _), "video can be grabbed");
        Check(GrabbableModal.LiveCount == 1 && UguiPokeSurfaces.Canvases.Count == 1, "one frame and pointer surface");
        Check(CanvasConversion.Ordered.Count == 1, "first reveal participates in the native window draw ladder");
        NativeVideoWindow.TryGetGrab(out var ownGrab);
        Check(ownGrab!.GrabRoot!.root.gameObject.Persistent, "grab holder survives scene unload with the movie");
        Check(NativeVideoWindow.OwnsGrab(ownGrab), "live movie owns its exact chrome");
        var orphan = new GrabbableModal();
        var ordinary = new GrabbableModal();
        var fixturePanel = new ConvertedPanel { HostGo = new GameObject("Other panel") };
        orphan.Build(fixturePanel, 1f, "Native video");
        ordinary.Build(fixturePanel, 1f, "Ordinary modal");
        ModalFallback.Converted.Add(new ModalFallback.WindowPanel { Grab = ordinary });
        var position = ownGrab.GrabRoot.position;
        foreach (var panel in CanvasConversion.Ordered)
        {
            Check(panel.ContentGraphic != null && panel.Target.gameObject.Components.Contains(panel.ContentGraphic),
                "movie declares its actual pixels as full-frame content");
            Check(panel.BaseSortingOrder == ModalFallback.ModalHostSortingOrder, "movie uses the ordinary modal draw tier");
        }
        for (int i = 0; i < 100; i++)
        {
            ModalFallback.SweepForTest();
            NativeVideoWindow.Tick();
        }
        Check(!ownGrab.Destroyed && NativeVideoWindow.OwnsGrab(ownGrab), "orphan sweep preserves the live movie holder");
        Check(ownGrab.GrabRoot.position.Equals(position), "orphan sweeps do not relocate the movie");
        Check(orphan.Destroyed && ModalFallback.Orphans == 1, "same-name orphan is still destroyed");
        Check(!ordinary.Destroyed && ModalFallback.LastSweepCount == 2, "ordinary modal ownership stays valid");
        ordinary.Destroy();
        ModalFallback.Converted.Clear();
        Check(NativeVideoWindow.Window == window && GrabbableModal.LiveCount == 1, "stable frames do not rebuild chrome");
        native.isPaused = true; native.isPlaying = false;
        NativeVideoWindow.Tick();
        Check(NativeVideoWindow.Window == window, "paused native frame remains visible");
        native.isPaused = false;
        NativeVideoWindow.Tick();
        Check(!NativeVideoWindow.Visible && GrabbableModal.LiveCount == 0 && UguiPokeSurfaces.Canvases.Count == 0,
            "native completion removes frame and interaction atomically");
        Check(!NativeVideoWindow.OwnsGrab(ownGrab), "completed movie no longer claims chrome");
        Check(CanvasConversion.Ordered.Count == 0, "movie completion removes the order-only registration");

        var mirror = new GameObject("Mirror").AddComponent<VideoPlayer>();
        mirror.texture = new Texture(); mirror.frame = 20; mirror.isPrepared = mirror.isPlaying = true;
        mirror.url = "/game/StreamingAssets/Movies/Heroes/Brute.mov";
        NativeVideoWindow.SetRemoteSource(mirror);
        NativeVideoWindow.Tick();
        Check(!NativeVideoWindow.Visible, "remote first frame waits for elected pose");
        NativeVideoWindow.SetRemotePose(new Vector3 { x = 12, y = 3, z = 4 }, new Quaternion(), 1.5f);
        NativeVideoWindow.Tick();
        Check(NativeVideoWindow.Visible, "remote-only playback gets same window");
        Check(NativeVideoWindow.TryGetGrab(out var remoteGrab) && ((IPanelGrabOwner)remoteGrab!).GrabRoot!.position.x == 12,
            "first remote reveal already carries elected pose");
        NativeVideoWindow.TryGetGrab(out var mirrorGrab);
        ModalFallback.SweepForTest();
        Check(!mirrorGrab!.Destroyed && NativeVideoWindow.OwnsGrab(mirrorGrab), "remote movie survives the same orphan sweep");
        Check(mirrorGrab.GrabRoot!.root.gameObject.Persistent, "remote holder shares the canvas lifetime");
        Check(NativeVideoWindow.NativePlayer == null && NativeVideoWindow.PlaybackKey == string.Empty,
            "remote playback never becomes native sender authority");
        native.isPlaying = true;
        NativeVideoWindow.Tick();
        Check(NativeVideoWindow.NativePlayer == native, "native playback retains authority over cosmetic mirror");
        Check(!NativeVideoWindow.TrySkipNativeIntro(), "elected mirror cannot invoke local native continuation");
        NativeVideoWindow.SetRemoteSource(null);
        NativeVideoWindow.Tick();
        uint generation = NativeVideoWindow.NativePlaybackGeneration;
        native.StartPlayback();
        Check(NativeVideoWindow.NativePlaybackGeneration != generation, "same-clip replay has a fresh playback identity");
        Check(native.frame == 0 && native.isPlaying && native.url.EndsWith("GH_CP_Intro.mov"), "presentation never writes native playback");
        var step = new GameObject("Campaign step").AddComponent<UIMapFTUEInitialStep>();
        var tracker = step.gameObject.AddComponent<ClickTrackerExtended>();
        step.SetTracker(tracker, "CP_Intro/GH_CP_Intro");
        Check(NativeVideoWindow.TrySkipNativeIntro() && step.Escapes == 1, "movie click calls the original active intro escape");
        tracker.enabled = false;
        Check(!NativeVideoWindow.TrySkipNativeIntro() && step.Escapes == 1, "stale disabled intro cannot receive a movie click");
        tracker.enabled = true;
        step.SetTracker(tracker, "CP_Intro/Other");
        Check(!NativeVideoWindow.TrySkipNativeIntro(), "different movie cannot drive the intro continuation");
        NativeVideoWindow.SetNativePresentationSuppressed(true);
        NativeVideoWindow.Tick();
        Check(!NativeVideoWindow.Visible && NativeVideoWindow.NativePlayer == native && native.isPlaying,
            "elected completion hides a late local tail without stopping native playback");
        NativeVideoWindow.SetNativePresentationSuppressed(false);
        NativeVideoWindow.Tick();
        WorldUIConfig.ConversionActive = false;
        NativeVideoWindow.Tick();
        Check(!NativeVideoWindow.Visible && GrabbableModal.LiveCount == 0, "VR off releases presentation");
        NativeVideoWindow.Shutdown();
        WorldUIConfig.ConversionActive = true;
        native.isPlaying = false;
        NativeVideoWindow.Tick();
        Check(!NativeVideoWindow.Visible, "shutdown drops remote source");

        foreach (string key in new[] { "Heroes/Brute.mov", "Heroes/Spellweaver.mov", "CP_Intro/GH_CP_Intro.mov" })
            Check(NativeVideoWindow.IsPlaybackKey(key), "native asset allowed: " + key);
        foreach (string key in new[] { "", "../secret.mov", "Heroes/../secret.mov", "Heroes/a/b.mov", "Heroes/..mov",
                     "Heroes/Brute.mp4", "https://example.com/Heroes/Brute.mov", "CP_Intro/Other.mov", "Heroes/Brute.mov?x", "Heroes/Brute:secret.mov" })
            Check(!NativeVideoWindow.IsPlaybackKey(key), "non-native path rejected: " + key);
        Console.WriteLine($"Native video production harness: {_assertions} assertions passed.");
    }
}
