using System;
using System.Collections;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

public static class InteractionProgram
{
    public static int Checks;
    public static string Metrics = "actual production Build/Tick/LateSyncHost with native Unity transforms";
    private static void Check(bool value, string message) { Checks++; if (!value) throw new Exception(message); }
    private static bool Close(float a, float b) => Mathf.Abs(a - b) <= .0002f;
    private static Vector2 WorldSize(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        return new Vector2(Vector3.Distance(corners[0], corners[3]), Vector3.Distance(corners[0], corners[1]));
    }
    private static ConvertedPanel Panel(Vector2 size, SharedWindowKind kind)
    {
        var native = new GameObject("native canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = native.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = native.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
        var target = new GameObject("original target", typeof(RectTransform)).GetComponent<RectTransform>();
        target.SetParent(native.transform, false); target.anchorMin = target.anchorMax = new Vector2(.5f, .5f); target.sizeDelta = size;
        var host = new GameObject("converted host", typeof(RectTransform), typeof(Canvas));
        host.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rect = host.GetComponent<RectTransform>(); rect.sizeDelta = size;
        target.SetParent(host.transform, false);
        var panel = new ConvertedPanel { Target = target, HostGo = host, HostRect = rect,
            SharedDesignFrame = size, SharedDesignKind = kind };
        return panel;
    }
    private static GrabbableModal Build(ConvertedPanel panel, float zoom, float dial)
    {
        PanelLayout.WorldScale = zoom; WorldUIConfig.CanvasScaleMm.Value = dial;
        var grab = new GrabbableModal();
        var position = new Vector3(3, 12, -7); var rotation = Quaternion.Euler(0, 127, 0);
        CanvasConversion.PlaceHost(panel, position, rotation,
            SharedWindowSize.RenderWorldScale(panel, zoom) * SharedWindowSizeLaw.ExtraScale(panel.HostRect.rect.size, dial));
        grab.Build(panel, SharedWindowSizeLaw.ExtraScale(panel.HostRect.rect.size, dial), "geometry proof");
        grab.Participation(SharedWindows.Online && panel.SharedDesignKind != SharedWindowKind.None);
        return grab;
    }
    public static IEnumerator Run()
    {
        SharedWindows.Online = true; GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = false;
        GloomhavenVR.Rig.VRRigDriver.BaseWorldScale = 12f;
        Vector2[] frames = { new(1920,1080), new(512,1021), new(1096,233), new(802,126) };
        foreach (Vector2 frame in frames)
        foreach (float zoom in new[] { 7.22f, 12.22f, 23.39f, 26.21f, 100f })
        foreach (float dial in new[] { .5f, 1f, 2.5f })
        {
            var panel = Panel(frame, SharedWindowKind.ScenarioStory);
            var grab = Build(panel, zoom, dial);
            Vector2 expected = SharedWindowSizeLaw.CommittedMm(frame) * .012f;
            Vector2 actual = WorldSize(panel.HostRect);
            Check(Close(actual.x, expected.x) && Close(actual.y, expected.y), "shared Build world extent ignores each client's zoom and canvas dial");
            PanelLayout.WorldScale = zoom * 3; grab.Tick(); grab.LateSyncHost();
            actual = WorldSize(panel.HostRect);
            Check(Close(actual.x, expected.x) && Close(actual.y, expected.y), "shared Update and LateUpdate retain the same canonical geometry");
            Vector3 origin = panel.HostGo.transform.position;
            grab.Carry(new Vector3(4, 15, -2), Quaternion.Euler(0, 37, 0), 1.317f); grab.Tick(); grab.LateSyncHost();
            actual = WorldSize(panel.HostRect);
            Check(Close(actual.x, expected.x * 1.32f) && Close(actual.y, expected.y * 1.32f), "explicit shared resize stays quantized while independent zoom is ignored");
            Check(Vector3.Distance(panel.HostGo.transform.position, grab.Frame.position) < .0001f && Quaternion.Angle(panel.HostGo.transform.rotation, grab.Frame.rotation) < .01f,
                "physical shared drag writes the same actual host pose in Update and LateUpdate");
            grab.Release();
            // Native first-fit changes pixels, then the original scale setter keeps the same host
            // centre. A late joiner builds from exactly this fitted rect at its independent zoom.
            var fitted = new Vector2(512,689); panel.HostRect.sizeDelta = fitted;
            grab.SetExtraScale(SharedWindowSizeLaw.ExtraScale(fitted, dial)); grab.Tick(); grab.LateSyncHost();
            Vector2 fittedActual = WorldSize(panel.HostRect);
            var joinPanel = Panel(fitted, SharedWindowKind.ScenarioStory); var joinGrab = Build(joinPanel, 37.9f, dial);
            joinGrab.Carry(grab.Frame.position, grab.Frame.rotation, 1.32f); joinGrab.Tick(); joinGrab.LateSyncHost();
            Vector2 joined = WorldSize(joinPanel.HostRect);
            Check(Close(joined.x, fittedActual.x) && Close(joined.y, fittedActual.y), "late join uses the original fitted geometry independent of its starting zoom");
            Check(Vector3.Distance(panel.HostGo.transform.position, grab.Frame.position) < .0001f, "first fit does not move an already grabbed host");
        }
        foreach (SharedWindowKind kind in new[] { SharedWindowKind.MapStory, SharedWindowKind.QuestConfirm, SharedWindowKind.Encounter, SharedWindowKind.Video, SharedWindowKind.RewardShowcase })
        {
            GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = true;
            var p = Panel(new Vector2(512,689), kind); var g = Build(p, 80f, 1f);
            Vector2 expected = SharedWindowSizeLaw.CommittedMm(p.HostRect.rect.size) * .19812f;
            Check(Close(WorldSize(p.HostRect).x, expected.x), "every shared kind uses stable parchment geometry on its first Build");
            GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Scale *= 2; g.Tick(); g.LateSyncHost();
            Check(Close(WorldSize(p.HostRect).x, expected.x), "a live shared window keeps its reference through later rig/frame transitions");
            GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Scale = 198.12f;
        }
        GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = false;
        SharedWindows.Online = false;
        var privatePanel = Panel(new Vector2(512,689), SharedWindowKind.ScenarioStory); var privateGrab = Build(privatePanel, 23f, 1f);
        float width = WorldSize(privatePanel.HostRect).x;
        Check(Close(width, SharedWindowSizeLaw.CommittedMm(privatePanel.HostRect.rect.size).x * .023f),
            "private window initially uses its own captured zoom");
        PanelLayout.WorldScale = 50f; privateGrab.Tick(); privateGrab.LateSyncHost();
        Check(Close(WorldSize(privatePanel.HostRect).x, width), "private window retains its original captured zoom after later zoom");
        SharedWindows.Online = true; privateGrab.Participation(true); privateGrab.Tick(); privateGrab.LateSyncHost();
        Check(Close(WorldSize(privatePanel.HostRect).x, SharedWindowSizeLaw.CommittedMm(privatePanel.HostRect.rect.size).x * .012f), "a standing private window adopts common geometry when the session begins");
        SharedWindows.Online = false; privateGrab.Participation(false); privateGrab.Tick(); privateGrab.LateSyncHost();
        Check(Close(WorldSize(privatePanel.HostRect).x, width), "leaving the shared session restores the captured private reference");
        SharedWindows.Online = true;
        var camera = new GameObject("host head", typeof(Camera)).GetComponent<Camera>();
        CanvasConversion.WorldCamera = camera;
        camera.transform.SetPositionAndRotation(new Vector3(7,18,-4), Quaternion.Euler(25,61,9));
        var hostPanel = Panel(new Vector2(1096,233), SharedWindowKind.ScenarioStory);
        hostPanel.RevealPending = true;
        var hostGrab = Build(hostPanel, 7.22f, 1f);
        ModalFallback.Register(hostPanel, hostGrab);
        SharedWindowSpawnBridge.InitialAuthor = () => true;
        Check(ModalFallback.ComputeHost(hostPanel, false, out Vector3 firstPosition, out Quaternion firstRotation, out _), "elected author computes its opening from the host head");
        Check(Mathf.Abs(firstPosition.y - camera.transform.position.y) < .0001f
            && Vector3.Dot(firstRotation * Vector3.up, Vector3.up) > .9999f,
            "host initial spawn is readable at eye level and yaw-only");
        camera.transform.SetPositionAndRotation(new Vector3(-40,88,19), Quaternion.Euler(-35,212,-12));
        PanelLayout.WorldScale = 26.21f;
        Check(ModalFallback.ComputeHost(hostPanel, true, out Vector3 replayPosition, out Quaternion replayRotation, out _)
            && Vector3.Distance(replayPosition, firstPosition) < .0001f && Quaternion.Angle(replayRotation, firstRotation) < .01f,
            "host final-fit replay retains the original opening gaze");
        hostGrab.SnapFrameTo(firstPosition, firstRotation);
        Check(SharedWindowSpawnBridge.TryReveal(hostPanel) && ModalFallback.SharedInitialPoseReady(SharedWindowKind.ScenarioStory),
            "initial author becomes ready only at the actual reveal edge");
        hostGrab.Carry(new Vector3(23,14,8), Quaternion.Euler(0,144,0), 1.17f); hostGrab.Tick(); hostGrab.Release();
        Check(ModalFallback.ComputeHost(hostPanel, false, out Vector3 retained, out _, out _)
            && Vector3.Distance(retained, hostGrab.Frame.position) < .0001f,
            "a shared opening already placed by a player never respawns at a later host gaze");

        var followerPanel = Panel(new Vector2(1096,233), SharedWindowKind.ScenarioStory);
        var followerGrab = Build(followerPanel, 23.39f, 1f);
        ModalFallback.Register(followerPanel, followerGrab);
        SharedWindowSpawnBridge.InitialAuthor = () => false;
        SharedWindowSpawnBridge.TryInitialPose = null;
        CanvasConversion.WorldCamera = null;
        Check(ModalFallback.ComputeHost(followerPanel, false, out Vector3 provisional, out _, out _)
            && provisional == Vector3.zero, "follower never reads its own camera while awaiting the initial shared pose");
        Check(!SharedWindowSpawnBridge.TryReveal(followerPanel) && !followerPanel.SharedSpawnPoseReady,
            "follower stays hidden while the current opening has no initial author pose");
        SharedWindowSpawnBridge.TryInitialPose = (SharedWindowKind kind, out Vector3 pos, out Quaternion rot, out float factor) =>
        { pos = firstPosition; rot = firstRotation; factor = 1.37f; return kind == SharedWindowKind.ScenarioStory; };
        Check(SharedWindowSpawnBridge.TryReveal(followerPanel) && followerPanel.SharedSpawnPoseReady,
            "follower reveals only after applying the shared current-opening pose");
        Check(Vector3.Distance(followerPanel.HostGo.transform.position, firstPosition) < .0001f
            && Quaternion.Angle(followerPanel.HostGo.transform.rotation, firstRotation) < .01f
            && Close(followerGrab.Frame.localScale.x, 1.37f), "first shared picture snaps to the exact shared pose and resize");
        Check(ModalFallback.ReplayDisabled(followerPanel), "adopting the first shared pose retires every provisional replay");
        var unrelated = Panel(new Vector2(400,300), SharedWindowKind.None);
        Check(SharedWindowSpawnBridge.TryReveal(unrelated), "private native windows never wait for shared transport");
        foreach (SharedWindowKind dedicated in new[] { SharedWindowKind.Video, SharedWindowKind.RewardShowcase })
            Check(SharedWindowSpawnBridge.TryReveal(Panel(new Vector2(400,300), dedicated)), "dedicated movie and reward handoffs retain their own reveal authority");
        SharedWindows.Online = false;
        var standing = Panel(new Vector2(802,126), SharedWindowKind.Encounter);
        var standingGrab = Build(standing, 17.5f, 1f);
        ModalFallback.Register(standing, standingGrab);
        Vector3 standingPosition = standing.HostGo.transform.position;
        SharedWindowSpawnBridge.InitialAuthor = () => true;
        SharedWindows.Online = true;
        Check(ModalFallback.SharedInitialPoseReady(SharedWindowKind.Encounter) && standing.SharedSpawnPoseReady,
            "already revealed host window becomes an initial pose publisher when a session starts");
        Check(Vector3.Distance(standing.HostGo.transform.position, standingPosition) < .0001f,
            "session start publishes the existing host pose without a new gaze spawn");
        yield return null;
    }
}
