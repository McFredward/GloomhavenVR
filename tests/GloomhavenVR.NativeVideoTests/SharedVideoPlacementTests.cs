using System;
using System.Linq;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.Video;

internal static class SharedVideoPlacementTests
{
    internal static void Run(Action<bool, string> check)
    {
        NativeVideoWindow.Shutdown();
        SharedWindows.Online = true;
        SharedWindowSpawnBridge.InitialAuthor = () => true;
        SharedWindowSpawnBridge.TryInitialPose = null;
        CanvasConversion.WorldCamera!.transform.position = new Vector3 { x = 19, y = 7, z = -2 };
        var mirror = new GameObject("Host cosmetic movie decoder").AddComponent<VideoPlayer>();
        mirror.url = "/game/StreamingAssets/Movies/Heroes/Brute.mov";
        mirror.texture = new Texture(); mirror.frame = 7; mirror.isPrepared = mirror.isPlaying = true;
        NativeVideoWindow.SetRemoteSource(mirror);
        NativeVideoWindow.Tick();
        check(NativeVideoWindow.Visible && NativeVideoWindow.SharedInitialPoseReady,
            "shared host can reveal a cosmetic decoder before any peer physical pose exists");
        check(NativeVideoWindow.TryGetGrab(out var hostGrab) && hostGrab!.GrabRoot!.position.x == 19,
            "shared movie's first physical placement follows the host head even with a remote decoder");
        NativeVideoWindow.SetRemotePose(new Vector3 { x = -400, y = -300, z = -200 }, new Quaternion(), 1.5f);
        NativeVideoWindow.Tick();
        check(hostGrab!.GrabRoot!.position.x == 19,
            "decoder-provided provisional pose never overrides the host's physical placement");
        check(mirror.isPlaying && mirror.frame == 7 && mirror.PlayCalls == 0,
            "host pose authoring neither restarts nor advances the elected decoder");

        NativeVideoWindow.Shutdown();
        SharedWindowSpawnBridge.InitialAuthor = () => false;
        var nativeOwner = new GameObject("Follower native movie owner").AddComponent<VideoCamera>();
        nativeOwner.m_Camera = nativeOwner.gameObject.AddComponent<Camera>();
        var native = nativeOwner.m_VideoPlayer = nativeOwner.gameObject.AddComponent<VideoPlayer>();
        VideoCamera.s_This = nativeOwner;
        native.url = "/game/StreamingAssets/Movies/Heroes/Spellweaver.mov";
        native.texture = new Texture(); native.frame = 9; native.isPrepared = native.isPlaying = true;
        NativeVideoWindow.Tick();
        var pending = CanvasConversion.Ordered.Single();
        check(!NativeVideoWindow.Visible && !NativeVideoWindow.SharedInitialPoseReady,
            "shared native follower stays hidden until the matching host physical pose arrives");
        bool hasFollowerGrab = NativeVideoWindow.TryGetGrab(out var followerGrab);
        check(!pending.HostCanvas.enabled && !pending.HostRaycaster.enabled
            && hasFollowerGrab && !followerGrab!.GrabVisible,
            "pending shared video disables picture hit surface and physical grabs together");
        check(NativeVideoWindow.OwnsGrab(followerGrab!),
            "pending shared video retains its exact grab holder through the orphan sweep");
        ModalFallback.SweepForTest();
        check(!followerGrab!.Destroyed,
            "waiting for the host does not destroy a pending movie's chrome");
        check(native.isPlaying && native.frame == 9 && native.PlayCalls == 0,
            "waiting for shared placement leaves native playback and continuation untouched");
        SharedWindowSpawnBridge.TryInitialPose = (SharedWindowKind kind, out Vector3 position,
            out Quaternion rotation, out float factor) =>
        { position = new Vector3 { x = 23, y = 11, z = -8 }; rotation = new Quaternion(); factor = 1.37f; return kind == SharedWindowKind.Video; };
        NativeVideoWindow.Tick();
        check(NativeVideoWindow.Visible && NativeVideoWindow.SharedInitialPoseReady
            && pending.HostCanvas.enabled && pending.HostRaycaster.enabled,
            "shared video becomes visible only after adopting the current host pose");
        check(followerGrab.GrabRoot!.position.x == 23 && followerGrab.GrabRoot.localScale.x == 1.37f,
            "first shared movie picture and interaction frame use the exact host pose and factor");
        followerGrab.GrabRoot.position = new Vector3 { x = 48, y = 10, z = -7 };
        NativeVideoWindow.Tick();
        check(followerGrab.GrabRoot.position.x == 48,
            "an initialized shared movie is never reseated by a repeated initial-pose callback");

        NativeVideoWindow.Shutdown();
        native.isPlaying = false;
        SharedWindowSpawnBridge.TryInitialPose = null;
        NativeVideoWindow.SetRemoteSource(mirror);
        NativeVideoWindow.Tick();
        check(!NativeVideoWindow.Visible && NativeVideoWindow.TryGetGrab(out _),
            "shared remote follower can prepare its hidden frame without a decoder-owned pose");
        NativeVideoWindow.SetRemoteSource(null);
        native.isPlaying = true;
        NativeVideoWindow.Tick();
        SharedWindows.Online = false;
        NativeVideoWindow.Tick();
        check(NativeVideoWindow.Visible,
            "ending the shared session cannot strand a prepared movie behind its initial pose gate");
        NativeVideoWindow.Shutdown();
        SharedWindowSpawnBridge.InitialAuthor = null;
        SharedWindowSpawnBridge.TryInitialPose = null;
        SharedWindows.Online = false;
    }
}
