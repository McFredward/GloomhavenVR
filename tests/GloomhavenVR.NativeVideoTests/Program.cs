using System;
using System.Linq;
using UnityEngine.EventSystems;
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

    private static NativeVideoClick MovieHandler() => CanvasConversion.Ordered.Single().Target.gameObject
        .Components.OfType<NativeVideoClick>().Single();

    private static void ClickMovie(string source)
    {
        var handler = MovieHandler();
        // Execute the production choke point used by UguiPointer.Release, then dispatch the
        // real component attached by NativeVideoWindow.Build. A direct TrySkip call missed506.
        bool withheld = UguiPointer.ShouldWithholdUnstartedWindowClick(handler.gameObject, source);
        Check(!withheld, "laser and poke delivery guard admits the actual movie handler");
        if (!withheld) ((IPointerClickHandler)handler).OnPointerClick(new PointerEventData());
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
        Check(!NativeVideoWindow.TrySkipNativeMovie(), "elected mirror cannot invoke local native continuation");
        NativeVideoWindow.SetRemoteSource(null);
        NativeVideoWindow.Tick();
        uint generation = NativeVideoWindow.NativePlaybackGeneration;
        native.StartPlayback();
        Check(NativeVideoWindow.NativePlaybackGeneration != generation, "same-clip replay has a fresh playback identity");
        Check(native.frame == 0 && native.isPlaying && native.url.EndsWith("GH_CP_Intro.mov"), "presentation never writes native playback");
        var step = new GameObject("Campaign step").AddComponent<UIMapFTUEInitialStep>();
        var tracker = step.gameObject.AddComponent<ClickTrackerExtended>();
        step.SetTracker(tracker, "CP_Intro/GH_CP_Intro");
        ClickMovie("laser-R");
        Check(step.Escapes == 1, "movie laser click calls the original active intro escape");
        ClickMovie("poke-L");
        Check(step.Escapes == 2, "movie fingertip click uses the same native escape");
        var pooled = new GameObject("Pooled confirmation").AddComponent<UIWindow>();
        var pooledClick = new GameObject("Movie");
        pooledClick.transform.SetParent(pooled.transform, false);
        Check(UguiPointer.ShouldWithholdUnstartedWindowClick(pooledClick, "laser-R"),
            "unstarted native confirmation remains protected even with matching movie name");
        pooled.HasGoneToStartingState = true;
        Check(!UguiPointer.ShouldWithholdUnstartedWindowClick(pooledClick, "poke-L"),
            "started native window remains clickable");
        pooled.HasGoneToStartingState = false; pooled.IsOpen = true;
        Check(!UguiPointer.ShouldWithholdUnstartedWindowClick(pooledClick, "laser-R"),
            "explicitly opened native window remains clickable");
        var otherChild = new GameObject("Other child");
        otherChild.transform.SetParent(NativeVideoWindow.Window!.transform, false);
        Check(UguiPointer.ShouldWithholdUnstartedWindowClick(otherChild, "laser-R"),
            "movie identity exemption never applies to arbitrary child controls");
        tracker.enabled = false;
        Check(!NativeVideoWindow.TrySkipNativeMovie() && step.Escapes == 2, "stale disabled intro cannot receive a movie click");
        tracker.enabled = true;
        step.SetTracker(tracker, "CP_Intro/Other");
        Check(!NativeVideoWindow.TrySkipNativeMovie(), "different movie cannot drive the intro continuation");
        var previousHandler = MovieHandler();
        native.url = "/game/StreamingAssets/Movies/Heroes/Brute.mov";
        NativeVideoWindow.Tick();
        owner.RewardInputLocked = true;
        previousHandler.OnPointerClick(new PointerEventData());
        Check(native.isPlaying && owner.Completions == 0, "stale movie handler cannot complete replacement movie");
        ClickMovie("laser-R");
        Check(!native.isPlaying && owner.Completions == 1 && !owner.RewardInputLocked,
            "hero click runs native completion and releases reward input lock");
        ClickMovie("laser-R");
        Check(owner.Completions == 1, "repeated click after native stop cannot complete twice");
        native.isPlaying = true;
        owner.Completed = () =>
        {
            native.url = "/game/StreamingAssets/Movies/Heroes/Spellweaver.mov";
            native.isPlaying = true;
        };
        ClickMovie("poke-L");
        Check(owner.Completions == 2 && native.isPlaying && native.url.EndsWith("Spellweaver.mov"),
            "native continuation may start the next hero without being stopped afterwards");
        owner.Completed = null;
        NativeVideoWindow.Tick();
        NativeVideoWindow.SetRemoteSource(mirror);
        NativeVideoWindow.SetRemotePose(new Vector3(), new Quaternion(), 1f);
        NativeVideoWindow.Tick();
        ClickMovie("laser-R");
        Check(owner.Completions == 2 && native.isPlaying && mirror.isPlaying,
            "mirror click cannot complete native hero or stop cosmetic peer playback");
        NativeVideoWindow.SetRemoteSource(null);
        NativeVideoWindow.Tick();
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
