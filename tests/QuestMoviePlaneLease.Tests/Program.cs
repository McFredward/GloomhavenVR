using System;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.Video;

internal static class Program
{
    private static int count;
    private static void Check(bool value, string label) { count++; if (!value) throw new Exception(label); }
    private static (FlatScreen Owner, VideoPlayer Player) Setup(bool ui = false)
    {
        NativeMovieCameraPlane.RestoreAll(); FlatScreen.CapturedSet.Clear();
        VRSession.IsRunning = true; WorldUIConfig.ConversionActive = true; QuestStandalonePlatform.Enabled = true;
        VRRigDriver.HeadCamera = new Camera();
        var owner = new FlatScreen(); FlatScreen._coverageOwner = owner;
        var player = new VideoPlayer { targetCamera = new Camera { targetTexture = ui ? owner._uiRt : owner._rt } };
        FlatScreen.CapturedSet.Add(player.targetCamera, new FlatScreen.CapturedCamera { Owner = owner, IsUi = ui });
        owner._backRenderer!.sharedMaterial = owner._screenMaterial;
        owner._stereo.Player = player; owner._stereo.Material = owner._screenMaterial;
        NativeVideoMovies.CameraPlaneSource = player;
        Time.frameCount++;
        return (owner, player);
    }
    private static void Main()
    {
        foreach (bool ui in new[] { false, true })
        {
            var state = Setup(ui); VideoPlayer player = state.Player;
            NativeMovieCameraPlane.RefreshQuestMovie();
            Check(player.targetCameraAlpha == 0f, "owned Quest movie reserves packed plane before native draw");
            Check(player.Subscribers == 1, "one exact decoder failure owner is registered");
            NativeMovieCameraPlane.BeforeRender(player.targetCamera);
            NativeMovieCameraPlane.AfterRender(player.targetCamera);
            Check(player.targetCameraAlpha == 0f, "Quest native plane remains hidden between camera renders");
            Check(Math.Abs(NativeMovieCameraPlane.PresentationAlpha(player) - .73f) < .0001f,
                "recorded eye retains the exact authored presentation alpha");
            player.targetCameraAlpha = .41f; NativeMovieCameraPlane.RefreshQuestMovie();
            Check(player.targetCameraAlpha == 0f && Math.Abs(NativeMovieCameraPlane.PresentationAlpha(player) - .41f) < .0001f,
                "later authored fade updates the persistent eye alpha");
            player.url = "Ambient/next.sbs-v1.mov";
            NativeMovieCameraPlane.RefreshQuestMovie();
            Check((string)typeof(NativeMovieCameraPlane).GetField("_startupUrl", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)! == player.url,
                "source revision is rebound before another native render");
            Check(player.Subscribers == 1, "source revision retires its old error subscription");
            state.Owner._visible = false; NativeMovieCameraPlane.RefreshQuestMovie();
            Check(Math.Abs(player.targetCameraAlpha - .41f) < .0001f && player.Subscribers == 0,
                "screen Hide restores the latest original alpha and removes decoder subscription");
            state.Owner._visible = true; NativeMovieCameraPlane.RefreshQuestMovie();
            Check(player.targetCameraAlpha == 0f && player.Subscribers == 1,
                "screen Show reacquires the exact surviving decoder without another native Play");
            player.Fail(); Check(Math.Abs(player.targetCameraAlpha - .41f) < .0001f && player.Subscribers == 0,
                "decoder failure immediately restores the latest original alpha");
            NativeMovieCameraPlane.RefreshQuestMovie();
            Check(Math.Abs(player.targetCameraAlpha - .41f) < .0001f && player.Subscribers == 0,
                "failed unchanged decoder is not silently reacquired on the next capture");
        }
        {
            // Same ordering as the actual startup prefix, followed by a brief
            // ordinary screen suppression. B711's one-shot prefix cannot rerun
            // when that surviving decoder returns to its captured screen.
            var state = Setup(true); VideoPlayer player = state.Player; player.targetCameraAlpha = 1f;
            NativeMovieCameraPlane.ReserveStartup(player);
            Check(player.targetCameraAlpha == 0f, "studio prefix reserves the packed plane before original Play");
            state.Owner._visible = false; NativeMovieCameraPlane.RefreshQuestMovie();
            Check(player.targetCameraAlpha == 1f, "startup owner loss restores its real native alpha one");
            state.Owner._visible = true;
            NativeMovieCameraPlane.BeforeRender(player.targetCamera); NativeMovieCameraPlane.AfterRender(player.targetCamera);
            Check(player.targetCameraAlpha == 0f && NativeMovieCameraPlane.PresentationAlpha(player) == 1f,
                "surviving packed studio source is suppressed across the next render without rerunning Play or prefix");
        }
        foreach (string fault in new[] { "hidden", "vr", "conversion", "head", "head-disabled", "player", "camera", "foreign-target",
            "target-lost", "split", "material", "renderer", "hierarchy", "renderer-material", "decoder-identity", "compositor", "surface", "capture-owner", "capture-absent" })
        {
            var state = Setup(); var owner = state.Owner; VideoPlayer player = state.Player;
            NativeMovieCameraPlane.RefreshQuestMovie();
            switch (fault)
            {
                case "hidden": owner._visible = false; break;
                case "vr": VRSession.IsRunning = false; break;
                case "conversion": WorldUIConfig.ConversionActive = false; break;
                case "head": VRRigDriver.HeadCamera = null; break;
                case "head-disabled": VRRigDriver.HeadCamera!.isActiveAndEnabled = false; break;
                case "player": player.isActiveAndEnabled = false; break;
                case "camera": player.targetCamera.isActiveAndEnabled = false; break;
                case "foreign-target": player.targetCamera.targetTexture = new RenderTexture(); break;
                case "target-lost": player.targetCamera.targetTexture!.Created = false; break;
                case "split": owner._splitRouting = false; break;
                case "material": owner._screenMaterial = new Material(); break;
                case "renderer": owner._backRenderer!.enabled = false; break;
                case "hierarchy": owner._backRenderer!.gameObject.activeInHierarchy = false; break;
                case "renderer-material": owner._backRenderer!.sharedMaterial = new Material(); break;
                case "decoder-identity": owner._stereo.Player = new VideoPlayer(); break;
                case "compositor": owner._stereo.Active = false; break;
                case "surface": owner._stereo.Created = false; break;
                case "capture-owner": FlatScreen.CapturedSet[player.targetCamera].Owner = new FlatScreen(); break;
                case "capture-absent": FlatScreen.CapturedSet.Clear(); break;
            }
            NativeMovieCameraPlane.RefreshQuestMovie();
            Check(Math.Abs(player.targetCameraAlpha - .73f) < .0001f && player.Subscribers == 0,
                "unsafe Quest movie ownership restores exact original alpha: " + fault);
        }
        {
            var state = Setup(); VideoPlayer first = state.Player;
            NativeMovieCameraPlane.RefreshQuestMovie();
            var second = new VideoPlayer { targetCamera = first.targetCamera, targetCameraAlpha = .26f };
            state.Owner._stereo.Player = second; NativeVideoMovies.CameraPlaneSource = second;
            NativeMovieCameraPlane.RefreshQuestMovie();
            Check(first.targetCameraAlpha == .73f && first.Subscribers == 0 && second.targetCameraAlpha == 0f && second.Subscribers == 1,
                "same-camera replacement releases previous decoder and reserves only the selected replacement");
            NativeMovieCameraPlane.RestoreAll();
            Check(second.targetCameraAlpha == .26f && second.Subscribers == 0, "full teardown restores the replacement decoder");
        }
        {
            var state = Setup(); NativeMovieCameraPlane.RefreshQuestMovie();
            NativeVideoMovies.CameraPlaneSource = null; NativeMovieCameraPlane.RefreshQuestMovie();
            Check(state.Player.targetCameraAlpha == .73f && state.Player.Subscribers == 0, "departed selected native source releases the persistent lease");
        }
        {
            var state = Setup(); VideoPlayer player = state.Player; QuestStandalonePlatform.Enabled = false;
            NativeMovieCameraPlane.RefreshQuestMovie();
            Check(player.targetCameraAlpha == .73f && player.Subscribers == 0, "desktop source receives no Quest persistent lease");
            NativeMovieCameraPlane.BeforeRender(player.targetCamera);
            Check(player.targetCameraAlpha == 0f, "desktop capture still borrows its scoped zero alpha");
            NativeMovieCameraPlane.AfterRender(player.targetCamera);
            Check(player.targetCameraAlpha == .73f, "desktop capture immediately returns the exact scoped alpha");
        }
        Check(Debug.Logs.Count <= 12, "Quest lease diagnostics remain bounded");
        NativeMovieCameraPlane.RestoreAll();
        Console.WriteLine("PASS " + count + " source-bound Quest movie lease and exact capture ownership assertions; GPU/headset output unverified.");
    }
}
