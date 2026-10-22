using System;
using System.Collections;
using System.Reflection;
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
        NativeGeometryReference();
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
        SpawnDistanceParity();
        VideoDistanceParity();
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
        CapturedReferenceParity();
        yield return null;
    }

    private static void SpawnDistanceParity()
    {
        ResetNativeReference(); UnityGameEditorRuntime.s_TileSize = new Vector3(1.8f, 0, 1);
        SharedWindows.Online = true;
        SharedWindowSpawnBridge.InitialAuthor = () => true;
        var head = new GameObject("fixed physical host head", typeof(Camera)).GetComponent<Camera>();
        CanvasConversion.WorldCamera = head;
        Vector3 eye = new(7, 18, -4);
        foreach (bool map in new[] { false, true })
        foreach (SharedWindowKind kind in new[] { SharedWindowKind.ScenarioStory, SharedWindowKind.MapStory,
            SharedWindowKind.QuestConfirm, SharedWindowKind.Encounter, SharedWindowKind.RewardShowcase })
        foreach (float pitch in new[] { 0f, 68f, 90f, -45f })
        foreach (int table in new[] { 0, 1, 2 })
        {
            Vector3 first = default; Quaternion firstRotation = default; bool haveFirst = false;
            foreach (float zoom in new[] { .25f, 7.22f, 23.39f, 100f, 900f })
            {
                GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = map;
                GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Scale = 198.12f;
                head.transform.SetPositionAndRotation(eye, Quaternion.Euler(pitch, 61, 9));
                Vector3 originalForward = head.transform.forward;
                CameraController.s_CameraController = table == 0 ? null! : new CameraController
                    { FocusPoint = new Vector3(0, table == 1 ? eye.y - 1 : eye.y + 20, 0) };
                VRModeStateMachine.TableInFrontOfPlayer = table != 0;
                var panel = Panel(new Vector2(512, 689), kind); panel.RevealPending = true;
                var grab = Build(panel, zoom, zoom < 10 ? .5f : 2.5f);
                ModalFallback.Register(panel, grab);
                float reference = grab.SharedReferenceWorldScale;
                Check(ModalFallback.ComputeHost(panel, false, out Vector3 position, out Quaternion rotation,
                    out float scale), "shared author produces a physical spawn in every map and table context");
                Check(Close(scale, reference)
                    && Mathf.Abs(Vector3.Distance(position, eye) - ModalFallback.ExpectedDistance * reference) < .001f,
                    "shared opening distance uses its captured reference independent of host live and saved zoom");
                Check(Mathf.Abs(position.y - eye.y) < .001f
                    && Vector3.Dot(rotation * Vector3.up, Vector3.up) > .9999f,
                    "shared downward and near-table spawn remains at eye level with yaw-only facing");
                if (haveFirst)
                    Check(Vector3.Distance(position, first) < .001f && Quaternion.Angle(rotation, firstRotation) < .01f,
                        "the same host head and gaze produces identical absolute shared spawn across every zoom");
                else { first = position; firstRotation = rotation; haveFirst = true; }
                // The real final-fit replay runs after head motion, zoom, and possibly a map-domain
                // transition. The original grab reference and opening head must both survive.
                PanelLayout.WorldScale = zoom * 7;
                GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = !map;
                GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Scale = 600f;
                head.transform.SetPositionAndRotation(new Vector3(-40, 88, 19), Quaternion.Euler(-35, 212, -12));
                Check(ModalFallback.ComputeHost(panel, true, out Vector3 replay, out Quaternion replayRotation,
                    out float replayScale, new Vector2(5, 80)) && Close(replayScale, reference)
                    && Vector3.Distance(replay, position) < .001f && Quaternion.Angle(replayRotation, rotation) < .01f,
                    "shared final-fit replay keeps original head and captured reference through zoom and map changes");
                Check(ModalFallback.ComputeWithPrivateReplay(panel, eye, originalForward, zoom,
                    out Vector3 repaired, out Quaternion repairedRotation, out float repairedScale)
                    && Close(repairedScale, reference) && Vector3.Distance(repaired, position) < .001f
                    && Quaternion.Angle(repairedRotation, rotation) < .01f,
                    "a replay from earlier private placement cannot import its old zoom distance or scale");
                if (table != 0)
                {
                    Vector3 raw = eye + originalForward * (ModalFallback.ExpectedDistance * reference);
                    Check(ModalFallback.ClampTableTerms(eye, originalForward, ref raw, reference,
                        Vector2.one, out float floor, out bool capWon)
                        && Close(floor, CameraController.s_CameraController!.FocusPoint.y + .30f * reference + 1)
                        && capWon && Vector3.Distance(raw, position) < .001f,
                        "real shared table clamp reads the fixed reference and preserves the requested eye-level distance");
                }
            }
        }
        CameraController.s_CameraController = null!; VRModeStateMachine.TableInFrontOfPlayer = false;
        GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Scale = 198.12f;
        foreach (bool map in new[] { false, true })
        {
            GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = map;
            head.transform.SetPositionAndRotation(eye, Quaternion.Euler(68, 61, 9));
            foreach (float zoom in new[] { .25f, 900f })
            {
                PanelLayout.WorldScale = zoom;
                var beforeGrab = Panel(new Vector2(512, 689), SharedWindowKind.ScenarioStory);
                Check(ModalFallback.ComputeHost(beforeGrab, false, out Vector3 position, out _, out float scale)
                    && Close(scale, SharedWindowSize.ReferenceWorldScale)
                    && Mathf.Abs(Vector3.Distance(position, eye) - ModalFallback.ExpectedDistance * scale) < .001f,
                    "initial conversion before the grab exists uses the common geometry reference instead of private zoom");
            }
        }
        GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = false;
        CanvasConversion.WorldCamera = null;
    }

    private static void VideoDistanceParity()
    {
        ResetNativeReference(); UnityGameEditorRuntime.s_TileSize = new Vector3(1.8f, 0, 1);
        var head = new GameObject("fixed video host head", typeof(Camera)).GetComponent<Camera>();
        Vector3 eye = new(7, 18, -4);
        head.transform.SetPositionAndRotation(eye, Quaternion.Euler(68, 61, 9));
        foreach (bool map in new[] { false, true })
        {
            SharedWindows.Online = true;
            GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = map;
            GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Scale = 198.12f;
            Vector3 first = default; bool haveFirst = false;
            foreach (float zoom in new[] { .25f, 7.22f, 23.39f, 100f, 900f })
            {
                PanelLayout.WorldScale = zoom;
                NativeVideoWindow.ComputeSpawn(head, null, out Vector3 position, out Quaternion rotation);
                float expected = PanelPlacement.SpawnDistance * SharedWindowSize.ReferenceWorldScale;
                Check(Mathf.Abs(Vector3.Distance(position, eye) - expected) < .001f
                    && position.y == eye.y && Vector3.Dot(rotation * Vector3.up, Vector3.up) > .9999f,
                    "shared video opening uses fixed absolute distance across independent host zoom");
                if (haveFirst) Check(Vector3.Distance(first, position) < .001f,
                    "shared video head and gaze spawn identically at every host zoom");
                else { first = position; haveFirst = true; }
                var panel = Panel(new Vector2(1280, 720), SharedWindowKind.Video);
                var grab = Build(panel, zoom, .5f); float captured = grab.SharedReferenceWorldScale;
                // Shared final placement follows the existing grab reference if the persistent
                // movie has already crossed a map-domain transition before a reveal edge.
                GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = !map;
                GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Scale = 600f;
                PanelLayout.WorldScale = zoom * 7;
                NativeVideoWindow.ComputeSpawn(head, grab, out Vector3 retained, out _);
                Check(Mathf.Abs(Vector3.Distance(retained, eye) - PanelPlacement.SpawnDistance * captured) < .001f,
                    "shared video uses its captured grab reference through later scene transitions");
                GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = map;
                GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Scale = 198.12f;
            }
        }
        SharedWindows.Online = false;
        GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = false;
        foreach (float zoom in new[] { .25f, 900f })
        {
            PanelLayout.WorldScale = zoom;
            NativeVideoWindow.ComputeSpawn(head, null, out Vector3 privatePosition, out _);
            Check(Mathf.Abs(Vector3.Distance(privatePosition, eye) - PanelPlacement.SpawnDistance * zoom) < .001f,
                "private video placement retains its existing player zoom behavior");
        }
        SharedWindows.Online = true;
    }

    private static void CapturedReferenceParity()
    {
        ResetNativeReference();
        UnityGameEditorRuntime.s_TileSize = new Vector3(1.72f, 0, 1);
        SharedWindows.Online = true;
        foreach (SharedWindowKind kind in new[] { SharedWindowKind.ScenarioStory, SharedWindowKind.MapStory,
            SharedWindowKind.QuestConfirm, SharedWindowKind.Encounter, SharedWindowKind.Video, SharedWindowKind.RewardShowcase })
        foreach (Vector2 pixels in new[] { new Vector2(1096, 233), new Vector2(512, 689) })
        {
            GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = true;
            var author = Panel(pixels, kind); var authorGrab = Build(author, 70f, 2.5f);
            authorGrab.Carry(new Vector3(11, 22, 33), Quaternion.Euler(0, 71, 0), 1.37f);
            authorGrab.Tick(); authorGrab.Release();
            float reference = authorGrab.SharedReferenceWorldScale;
            Check(Close(reference, 198.12f), "the source captures its actual map-domain reference");
            Vector2 authored = WorldSize(author.HostRect);
            // The persistent source has left the map, while a late viewer starts in a different
            // local map mode. Actual Build captures Hex11.47; the wire must supply the old198.12.
            GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = false;
            Check(authorGrab.SharedReferenceWorldScale == reference,
                "the author publishes the captured reference after its scene domain changes");
            var follower = Panel(pixels, kind); follower.RevealPending = follower.RenderHidden = true;
            var followerGrab = Build(follower, 3.7f, .5f);
            followerGrab.Carry(new Vector3(-3, 5, -7), Quaternion.Euler(0, 123, 0), 1.37f);
            followerGrab.Tick(); followerGrab.Release();
            Check(Close(followerGrab.SharedReferenceWorldScale, 1.72f / .15f)
                && WorldSize(follower.HostRect).x < authored.x / 10f,
                "the fixture reaches different reference domains for the same kind and authored pixels");
            Vector3 framePosition = followerGrab.Frame.position, hostPosition = follower.HostGo.transform.position;
            Quaternion frameRotation = followerGrab.Frame.rotation, hostRotation = follower.HostGo.transform.rotation;
            Vector3 frameScale = followerGrab.Frame.localScale;
            followerGrab.ApplySharedReferenceWorldScale(reference);
            Check(followerGrab.SharedReferenceWorldScale == reference,
                "a follower adopts the author reference independently of its local map domain");
            Vector2 immediate = WorldSize(follower.HostRect);
            Check(Close(immediate.x, authored.x) && Close(immediate.y, authored.y),
                "reference adoption updates the actual host before any first reveal or pose tick");
            Check(followerGrab.Frame.position == framePosition && followerGrab.Frame.localScale == frameScale
                && Quaternion.Angle(followerGrab.Frame.rotation, frameRotation) < .001f
                && follower.HostGo.transform.position == hostPosition
                && Quaternion.Angle(follower.HostGo.transform.rotation, hostRotation) < .001f,
                "reference adoption preserves the exact physical frame and manual factor");
            foreach (float invalid in new[] { 0f, -12f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                followerGrab.ApplySharedReferenceWorldScale(invalid);
                Check(followerGrab.SharedReferenceWorldScale == reference
                    && Close(WorldSize(follower.HostRect).x, authored.x),
                    "invalid reference values cannot damage the captured geometry");
            }
            // The ordinary imported pose follows reference adoption. Native show runs afterwards.
            followerGrab.SnapFrameTo(authorGrab.Frame.position, authorGrab.Frame.rotation);
            follower.RevealPending = follower.RenderHidden = false;
            GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = true;
            GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Scale = 600f;
            PanelLayout.WorldScale = 230f;
            followerGrab.Tick(); followerGrab.LateSyncHost();
            Check(Close(WorldSize(follower.HostRect).x, authored.x)
                && Close(WorldSize(follower.HostRect).y, authored.y),
                "adopted reference survives reveal, independent zoom and a later map-domain change");
            GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Scale = 198.12f;
        }

        // A native reward pose can arrive before its local conversion/grab exists. Its complete
        // production cache must carry the reference through that gap along with the pose/factor.
        GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = false;
        var rewardWindow = new UIWindow(); RewardShowcase.Window = rewardWindow; RewardShowcase.ContentKey = 0x12345678;
        Vector3 rewardPosition = new(17, 23, -9); Quaternion rewardRotation = Quaternion.Euler(0, 79, 0);
        RewardShowcasePlacement.SetInitialPose(RewardShowcase.ContentKey, rewardPosition, rewardRotation,
            1.37f, ready: true, referenceScale: 198.12f);
        Check(!RewardShowcasePlacement.ApplyInitialPose(rewardWindow, null),
            "a reward awaiting conversion retains its reference without fabricating a grab");
        var rewardPanel = Panel(new Vector2(512,689), SharedWindowKind.RewardShowcase);
        rewardPanel.RevealPending = rewardPanel.RenderHidden = true;
        var rewardGrab = Build(rewardPanel, 4f, .5f);
        ModalFallback.Register(rewardPanel, rewardGrab, rewardWindow);
        Check(RewardShowcasePlacement.ApplyInitialPose(rewardWindow, rewardGrab)
            && rewardGrab.SharedReferenceWorldScale == 198.12f,
            "a reward reference received before conversion reaches the later actual grab");
        Vector2 rewardExpected = SharedWindowSizeLaw.CommittedMm(rewardPanel.HostRect.rect.size) * (.19812f * 1.37f);
        Check(Close(WorldSize(rewardPanel.HostRect).x, rewardExpected.x)
            && Close(WorldSize(rewardPanel.HostRect).y, rewardExpected.y)
            && rewardGrab.Frame.position == rewardPosition && Close(rewardGrab.Frame.localScale.x, 1.37f),
            "the first reward picture uses its cached author reference with the same pose and manual factor");
        RewardShowcasePlacement.ClearInitialPose();
        RewardShowcase.ContentKey++;
        RewardShowcasePlacement.SetInitialPose(RewardShowcase.ContentKey, rewardPosition, rewardRotation, 1f);
        var freshReward = Panel(new Vector2(512,689), SharedWindowKind.RewardShowcase);
        freshReward.RevealPending = freshReward.RenderHidden = true;
        var freshGrab = Build(freshReward, 5f, 1f);
        ModalFallback.Register(freshReward, freshGrab, rewardWindow);
        float freshReference = freshGrab.SharedReferenceWorldScale;
        Check(RewardShowcasePlacement.ApplyInitialPose(rewardWindow, freshGrab)
            && freshGrab.SharedReferenceWorldScale == freshReference,
            "a new reward without a supplied reference cannot inherit the previous reward's domain");
        RewardShowcasePlacement.ClearInitialPose(); RewardShowcase.Window = null; RewardShowcase.ContentKey = 0;
        ResetNativeReference(); UnityGameEditorRuntime.s_TileSize = new Vector3(1.8f, 0, 1);
    }

    private static void ResetNativeReference()
    {
        typeof(SharedWindowSize).GetField("NativeHexScale", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, 0f);
        typeof(SharedWindowSize).GetField("NativeHexReadAttempted", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
        GloomhavenVR.WorldUI.Resources.Reads = 0;
        GloomhavenVR.WorldUI.Resources.Hex = null;
        GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = false;
    }

    private static void NativeGeometryReference()
    {
        ResetNativeReference();
        UnityGameEditorRuntime.s_TileSize = new Vector3(1.72f, 0, 1);
        foreach (float rig in new[] { 0f, 1f, 28f })
        {
            GloomhavenVR.Rig.VRRigDriver.BaseWorldScale = rig;
            Check(Close(SharedWindowSize.ReferenceWorldScale, 1.72f / .15f),
                "native Hex reference ignores menu and torn-down rig bases");
        }
        Check(UnityGameEditorRuntime.s_TileSize.x == 1.72f,
            "shared size never writes the native tile or LOS geometry");
        UnityGameEditorRuntime.s_TileSize = Vector3.zero;
        Check(Close(SharedWindowSize.ReferenceWorldScale, 1.72f / .15f),
            "successful original Hex geometry survives later rig and native-field gaps");
        ResetNativeReference();
        var hex = new GameObject("original Hex collider");
        hex.transform.localScale = new Vector3(4, 2, 3);
        hex.AddComponent<BoxCollider>().size = new Vector3(1.72f, .1f, 1);
        GloomhavenVR.WorldUI.Resources.Hex = hex;
        Check(Close(SharedWindowSize.ReferenceWorldScale, 1.72f / .15f),
            "pre-initialization reference reads the original collider size without prefab transform scale");
        Check(UnityGameEditorRuntime.s_TileSize == Vector3.zero,
            "pre-initialization asset reads leave original native state untouched");
        SharedWindowSize.ReferenceWorldScale.ToString();
        Check(GloomhavenVR.WorldUI.Resources.Reads == 1, "original Hex geometry is captured once and reused without resource scans");
        ResetNativeReference();
        var rendered = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(rendered.GetComponent<BoxCollider>());
        rendered.transform.localScale = new Vector3(3, 2, 1);
        GloomhavenVR.WorldUI.Resources.Hex = rendered;
        Check(Close(SharedWindowSize.ReferenceWorldScale, rendered.GetComponent<Renderer>().bounds.size.x / .15f),
            "original renderer fallback matches native initialization when the Hex has no collider");
        ResetNativeReference();
        Check(SharedWindowSize.ReferenceWorldScale == 12f && SharedWindowSize.ReferenceWorldScale == 12f,
            "missing original Hex preserves the native nonblocking 12x fallback");
        Check(GloomhavenVR.WorldUI.Resources.Reads == 1, "missing original Hex does not trigger a repeated resource scan");
        UnityGameEditorRuntime.s_TileSize = new Vector3(1.8f, 0, 1);
        Check(Close(SharedWindowSize.ReferenceWorldScale, 12f), "late native initialization can fill an unavailable original reference");
        ResetNativeReference();
        UnityGameEditorRuntime.s_TileSize = new Vector3(1.8f, 0, 1);
    }
}
