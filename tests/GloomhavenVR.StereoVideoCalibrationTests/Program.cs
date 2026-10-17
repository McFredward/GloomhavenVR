using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;
using FixtureFile = GloomhavenVR.WorldUI.File;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool condition, string reason)
    {
        _assertions++;
        if (!condition) throw new Exception("Calibration assertion: " + reason);
    }
    private static bool Near(float a, float b) => Math.Abs(a - b) < .0001f;
    private static VideoPlayer Player => VideoPlayer.All.Last();
    private static RawImage? Image => GameObject.All.SelectMany(g => g.Components).OfType<RawImage>().LastOrDefault(i => !i.Destroyed);
    private static void Tick(int count = 1) { for (int i = 0; i < count; i++) StereoVideoCalibration.Tick(); }
    private static void Reset()
    {
        StereoVideoCalibration.Stop();
        foreach (string name in new[] { "_checkedArguments", "_requested", "_attempted", "_finished", "_stereo" })
            typeof(StereoVideoCalibration).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
        GameObject.All.Clear(); VideoPlayer.All.Clear(); UguiPokeSurfaces.Canvases.Clear();
        UguiPokeSurfaces.Registrations = UguiPokeSurfaces.Removals = 0;
        StereoVideoSurface.AvailabilityChecks = StereoVideoSurface.Attachments = StereoVideoSurface.Disposals = 0;
        StereoVideoSurface.Available = StereoVideoSurface.AttachSucceeds = true;
        FixtureFile.Calls = 0; FixtureFile.Present = true; FixtureFile.LastPath = "";
        SceneController.Instance = new(); SceneController.Reads = 0;
        VRSession.IsRunning = WorldUIConfig.ConversionActive = true; FFSNetwork.IsOnline = false;
        CanvasConversion.WorldCamera = new GameObject("FixtureHead").AddComponent<Camera>();
        Loc.CurrentLanguage = "English"; PanelLayout.WorldScale = 1f; VRLog.Lines.Clear(); VRLayers.Applies = 0;
    }
    private static void Policy()
    {
        Check(StereoVideoCalibrationPolicy.Requested(new[] { "game", "--ghvr-stereo-video-calibration" }), "exact command-line argument enables the explicit pilot");
        foreach (string arg in new[] { "--GHVR-STEREO-VIDEO-CALIBRATION", "--ghvr-stereo-video-calibration=true", "--ghvr-stereo-video-calibration-extra", "prefix--ghvr-stereo-video-calibration", "" })
            Check(!StereoVideoCalibrationPolicy.Requested(new[] { "game", arg }), "argument matching is exact and case sensitive");
        foreach (bool vr in new[] { false, true }) foreach (bool conversion in new[] { false, true })
        foreach (bool online in new[] { false, true }) foreach (string scene in new[] { "MainMenu", "MainMenu_gamepad", "Gloomhaven_unified", "MainMenu_extra", "Scenario", "" })
            Check(StereoVideoCalibrationPolicy.Allowed(vr, conversion, scene, online) ==
                  (vr && conversion && !online && (scene == "MainMenu" || scene == "MainMenu_gamepad")), "VR offline native menu gates are exact");
        Check(StereoVideoCalibrationPolicy.MoviePath("fixture-plugin", true) == Path.Combine("fixture-plugin", "Movies", "Calibration", "calibration-sbs.mp4"), "stereo file name is explicit and mod-local");
        Check(StereoVideoCalibrationPolicy.MoviePath("fixture-plugin", false) == Path.Combine("fixture-plugin", "Movies", "Calibration", "calibration-mono.mp4"), "mono file name is explicit and mod-local");
        Check(StereoVideoCalibrationPolicy.ValidDimensions(3840, 1080, true), "full-SBS dimensions accepted");
        Check(StereoVideoCalibrationPolicy.ValidDimensions(1920, 1080, false), "mono dimensions accepted");
        Check(!StereoVideoCalibrationPolicy.ValidDimensions(1920, 1080, true), "mono cannot be mistaken for full-SBS");
        Check(!StereoVideoCalibrationPolicy.ValidDimensions(3840, 1080, false), "packed SBS cannot be used as mono");
    }
    private static void NoFlag()
    {
        Reset(); int objects = GameObject.All.Count; Tick(100);
        Check(FixtureFile.Calls == 0 && StereoVideoSurface.AvailabilityChecks == 0 && VideoPlayer.All.Count == 0,
            "no exact flag performs no file, shader or decoder work");
        Check(GameObject.All.Count == objects && SceneController.Reads == 0 && VRLog.Lines.Count == 0,
            "normal startup performs no calibration scene polling, canvas or logging");
    }
    private static void Gates()
    {
        foreach (string mode in new[] { "not-vr", "no-conversion", "online", "scenario", "unknown-scene", "no-camera" })
        {
            Reset();
            if (mode == "not-vr") VRSession.IsRunning = false;
            if (mode == "no-conversion") WorldUIConfig.ConversionActive = false;
            if (mode == "online") FFSNetwork.IsOnline = true;
            if (mode == "scenario") SceneController.Instance!.Scene = "Scenario";
            if (mode == "unknown-scene") SceneController.Instance = null;
            if (mode == "no-camera") CanvasConversion.WorldCamera = null;
            Tick(10);
            Check(FixtureFile.Calls == 0 && StereoVideoSurface.AvailabilityChecks == 0 && VideoPlayer.All.Count == 0,
                "disallowed gate performs no calibration media work: " + mode);
            VRSession.IsRunning = WorldUIConfig.ConversionActive = true; FFSNetwork.IsOnline = false;
            SceneController.Instance = new() { Scene = "MainMenu_gamepad" };
            CanvasConversion.WorldCamera ??= new GameObject("Head").AddComponent<Camera>();
            Tick(); Check(VideoPlayer.All.Count == 1 && Player.Prepares == 1, "waiting for allowed gamepad menu does not consume the attempt");
            var player = Player;
            FFSNetwork.IsOnline = true; Tick();
            Check(player.Stops == 1 && player.Destroyed && player.PreparedSubscribers == 0 && player.ErrorSubscribers == 0,
                "leaving offline menu stops and detaches the decoder");
            FFSNetwork.IsOnline = false; Tick(10);
            Check(VideoPlayer.All.Count == 1 && player.Prepares == 1, "returning to menu never restarts a completed attempt");
        }
    }
    private static void Ready(VideoPlayer player, bool stereo = true)
    { player.isPrepared = true; player.texture = new Texture { width = stereo ? 3840 : 1920, height = 1080 }; player.frame = 0; }
    private static void Stereo()
    {
        Reset();
        var native = new GameObject("NativeMovie").AddComponent<VideoPlayer>();
        native.url = "native-unchanged.mov"; native.frame = 71; native.isPrepared = true;
        native.texture = new Texture { width = 1920, height = 1080 };
        Tick(); var player = Player;
        Check(VideoPlayer.All.Count == 2 && !ReferenceEquals(native, player) && player.Prepares == 1 && player.Plays == 0, "one independent decoder prepares once");
        Check(!player.playOnAwake && player.isLooping && player.renderMode == VideoRenderMode.APIOnly && player.audioOutputMode == VideoAudioOutputMode.None,
            "calibration decoder is silent APIOnly with explicit prepare and loop");
        Check(player.url == Path.Combine(Path.GetDirectoryName(typeof(StereoVideoCalibration).Assembly.Location)!, "Movies", "Calibration", "calibration-sbs.mp4"), "stereo path is beside the actual plugin assembly");
        Check(Image == null && UguiPokeSurfaces.Canvases.Count == 0, "unprepared decoder has no picture");
        player.texture = new Texture { width = 3840, height = 1080 }; player.frame = 0; Tick();
        Check(Image == null, "prepared flag gates picture even when texture and frame exist");
        player.isPrepared = true; player.texture = null; Tick(); Check(Image == null, "prepared texture gates picture");
        player.texture = new Texture { width = 3840, height = 1080 }; player.frame = -1; Tick();
        Check(Image == null, "first decoded frame gates picture");
        player.EmitPrepared(); Check(player.Plays == 1, "only owned prepare callback starts decoder");
        Ready(player); Tick(); var image = Image!;
        Check(image != null && ReferenceEquals(image.texture, player.texture) && StereoVideoSurface.Attachments == 1, "first valid decoded frame attaches one stereo surface");
        Check(UguiPokeSurfaces.Canvases.Count == 1 && UguiPokeSurfaces.Registrations == 1 && VRLayers.Applies == 1, "picture has exactly one pointer registration and VR layer application");
        var rect = (RectTransform)image!.transform;
        Check(Near(rect.sizeDelta.x / rect.sizeDelta.y, 16f / 9f), "window aspect comes from per-eye dimensions");
        Check(Near(rect.position.z, 1.6f) && Near(rect.localScale.x * rect.sizeDelta.x, 1.2f) && Near(rect.rotation.Forward.y, 0), "fixed prepared window has documented distance width and yaw only");
        var label = GameObject.All.SelectMany(g => g.Components).OfType<TextMeshProUGUI>().Single();
        Check(label.text.Contains("Click") && !label.raycastTarget, "English close hint does not steal picture clicks");
        Loc.CurrentLanguage = "German"; Tick(); Check(label.text.Contains("Schließen"), "German close hint updates through production localization");
        Loc.CurrentLanguage = "French"; Tick(); Check(label.text.Contains("Click"), "unknown language uses English close hint");
        var replacement = new Texture { width = 3840, height = 1080 }; player.texture = replacement; Tick();
        Check(ReferenceEquals(image.texture, replacement), "prepared decoder texture updates the existing picture");
        CanvasConversion.WorldCamera!.transform.position = new Vector3(90, 10, 80); Tick(100);
        Check(Near(rect.position.z, 1.6f) && StereoVideoSurface.Attachments == 1 && player.Prepares == 1, "stable playback never follows head rebuilds surface or restarts decoder");
        var alien = new GameObject("Alien").AddComponent<StereoVideoCalibrationClick>();
        alien.OnPointerClick(new PointerEventData());
        Check(player.Stops == 0 && !StereoVideoCalibration.OwnsClick(alien.gameObject), "unowned click cannot close calibration");
        Check(StereoVideoCalibration.OwnsClick(image.gameObject) && !StereoVideoCalibration.OwnsClick(label.gameObject), "click ownership is the exact picture object");
        var click = image.gameObject.Components.OfType<StereoVideoCalibrationClick>().Single();
        click.OnPointerClick(new PointerEventData());
        Check(player.Stops == 1 && player.Destroyed && !player.gameObject.Active && image.Destroyed, "owned picture click stops and destroys calibration");
        Check(player.PreparedSubscribers == 0 && player.ErrorSubscribers == 0, "close detaches prepare and error callbacks");
        Check(StereoVideoSurface.Disposals == 1 && UguiPokeSurfaces.Removals == 1 && UguiPokeSurfaces.Canvases.Count == 0, "close disposes surface and unregisters pointer canvas");
        click.OnPointerClick(new PointerEventData()); StereoVideoCalibration.Stop(); Tick(20);
        Check(VideoPlayer.All.Count == 2 && player.Stops == 1 && StereoVideoSurface.Disposals == 1 && FixtureFile.Calls == 1, "close and shutdown are idempotent and never restart");
        Check(native.url == "native-unchanged.mov" && native.frame == 71 && native.isPrepared && native.texture!.width == 1920 && native.Prepares == 0 && native.Plays == 0 && native.Stops == 0 && !native.Destroyed && native.playOnAwake && !native.isLooping && native.renderMode == VideoRenderMode.CameraNearPlane && native.audioOutputMode == VideoAudioOutputMode.Direct,
            "native player content lifecycle render and audio remain untouched");
    }
    private static void Mono()
    {
        Reset(); StereoVideoSurface.Available = false; Tick(); var player = Player;
        Check(player.url.EndsWith(Path.Combine("Movies", "Calibration", "calibration-mono.mp4")), "missing shader selects the mono companion");
        Ready(player, false); Tick();
        Check(Image != null && StereoVideoSurface.Attachments == 0 && UguiPokeSurfaces.Canvases.Count == 1, "mono companion creates ordinary picture without stereo attachment");
        Check(Near(((RectTransform)Image!.transform).sizeDelta.y, 720f), "mono companion preserves 16:9 aspect");
        Check(VRLog.Lines.Any(line => line.Contains("using the mono companion")), "mono fallback records bounded failure context");
        StereoVideoCalibration.Stop(); Check(StereoVideoSurface.Disposals == 0 && Player.Stops == 1, "mono teardown releases only its owned resources");
    }
    private static void Errors()
    {
        Reset(); FixtureFile.Present = false; Tick(20);
        Check(FixtureFile.Calls == 1 && VideoPlayer.All.Count == 0 && VRLog.Lines.Count(line => line.Contains("movie missing")) == 1, "missing movie refuses once without decoder retry");
        foreach (bool prepared in new[] { false, true })
        {
            Reset(); Tick(); var player = Player;
            if (prepared) { Ready(player); Tick(); }
            var oldPrepared = player.CapturePrepared(); var oldError = player.CaptureError();
            var other = new GameObject("Unrelated").AddComponent<VideoPlayer>();
            oldPrepared!(other); oldError!(other, "unrelated");
            Check(player.Plays == 0 && player.Stops == 0 && other.Plays == 0, "callbacks ignore unowned decoder identity");
            player.EmitError("fixture decode failure"); Tick(20);
            Check(player.Stops == 1 && player.Destroyed && FixtureFile.Calls == 1, "decoder error closes once without restart");
            Check(player.PreparedSubscribers == 0 && player.ErrorSubscribers == 0, "error teardown removes both event subscribers");
            int logs = VRLog.Lines.Count;
            oldPrepared!(player); oldError!(player, "late error");
            Check(player.Plays == 0 && VRLog.Lines.Count == logs, "late queued callbacks cannot resurrect or report an ended decoder");
            Check(UguiPokeSurfaces.Canvases.Count == 0 && StereoVideoSurface.Disposals == (prepared ? 1 : 0), "error cleans prepared and unprepared ownership correctly");
        }
    }
    private static void Dimensions()
    {
        foreach (var size in new[] { (1920, 1080), (3840, 1078), (0, 0), (7680, 1080) })
        {
            Reset(); Tick(); var player = Player; Ready(player);
            player.texture!.width = size.Item1; player.texture.height = size.Item2; Tick();
            Check(Image == null && player.Stops == 1 && player.Destroyed, "invalid decoded dimensions refuse before picture creation");
            Tick(20); Check(FixtureFile.Calls == 1 && VideoPlayer.All.Count == 1, "invalid layout never retries");
        }
        Reset(); Tick(); Ready(Player); StereoVideoSurface.AttachSucceeds = false; Tick();
        Check(Image == null && Player.Stops == 1 && UguiPokeSurfaces.Canvases.Count == 0, "attachment failure stops instead of displaying unsplit SBS");
    }
    private static void Shutdown()
    {
        Reset(); StereoVideoCalibration.Stop(); Tick();
        Check(VideoPlayer.All.Count == 1 && Player.Prepares == 1, "shutdown before any attempt still permits the later allowed menu");
        foreach (string mode in new[] { "preparing", "prepared", "online", "scene", "vr", "conversion", "camera" })
        {
            Reset(); Tick(); var player = Player;
            if (mode != "preparing") { Ready(player); Tick(); }
            if (mode == "online") FFSNetwork.IsOnline = true;
            else if (mode == "scene") SceneController.Instance!.Scene = "Scenario";
            else if (mode == "vr") VRSession.IsRunning = false;
            else if (mode == "conversion") WorldUIConfig.ConversionActive = false;
            else if (mode == "camera") CanvasConversion.WorldCamera = null;
            else StereoVideoCalibration.Stop();
            Tick();
            Check(player.Stops == 1 && player.Destroyed && player.PreparedSubscribers == 0 && player.ErrorSubscribers == 0 && UguiPokeSurfaces.Canvases.Count == 0,
                "shutdown or lost gate releases decoder events and pointer surface: " + mode);
            Check(StereoVideoSurface.Disposals == (mode == "preparing" ? 0 : 1), "shutdown disposes exactly the prepared surface: " + mode);
        }
    }
    private static void ExternalDestroy()
    {
        foreach (bool wholeRoot in new[] { true, false })
        {
            Reset(); Tick(); var player = Player; Ready(player); Tick();
            var stalePrepared = player.CapturePrepared(); var staleError = player.CaptureError();
            UnityEngine.Object.Destroy(wholeRoot ? player.gameObject : player);
            Tick();
            Check(StereoVideoSurface.Disposals == 1, "external Unity destruction still disposes managed eye surface");
            Check(Image == null && !StereoVideoCalibration.OwnsClick(player.gameObject), "external Unity destruction clears visible and click ownership");
            int players = VideoPlayer.All.Count; Tick(10);
            Check(VideoPlayer.All.Count == players && FixtureFile.Calls == 1, "external destruction finishes the attempt without restart");
            int plays = player.Plays, logs = VRLog.Lines.Count;
            stalePrepared!(player); staleError!(player, "queued after external destruction");
            Check(player.Plays == plays && VRLog.Lines.Count == logs, "fake-null stale callbacks cannot start or report a destroyed decoder");
        }
    }
    private static int Main(string[] args)
    {
        string scenario = args[0];
        try
        {
            switch (scenario)
            {
                case "policy": Policy(); break;
                case "no-flag": NoFlag(); break;
                case "gates": Gates(); break;
                case "stereo": Stereo(); break;
                case "mono": Mono(); break;
                case "errors": Errors(); break;
                case "dimensions": Dimensions(); break;
                case "shutdown": Shutdown(); break;
                case "external-destroy": ExternalDestroy(); break;
                default: throw new Exception("Unknown scenario: " + scenario);
            }
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", scenario, assertions = _assertions }));
            return 0;
        }
        catch (Exception error) { Console.WriteLine(error); return 1; }
        finally { StereoVideoCalibration.Stop(); }
    }
}
