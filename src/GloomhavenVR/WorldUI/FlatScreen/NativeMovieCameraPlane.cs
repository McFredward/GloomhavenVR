using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Video;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Hide only the packed native video PLANE during its captured camera draw. Startup's
/// actual camera is orthographic/UI-only: discarding its output would also discard
/// live logos/widgets. Its transparent glass therefore keeps every native UI draw,
/// while the background quad presents the decoder's independent recorded eye.
/// No decoder mode/target/clock/callback is changed. Nested renders retain their lease.
/// </summary>
internal static class NativeMovieCameraPlane
{
    private readonly struct Lease
    {
        internal readonly Camera Camera;
        internal readonly VideoPlayer Player;
        internal readonly float Alpha;
        internal Lease(Camera camera, VideoPlayer player) { Camera = camera; Player = player; Alpha = player.targetCameraAlpha; }
        internal void Restore()
        {
            if (Player != null && Player.targetCameraAlpha == 0f) Player.targetCameraAlpha = Alpha;
        }
    }
    private static readonly Stack<Lease> Leases = new(4);
    private static int _frame = -1;
    private static VideoPlayer? _startupPlayer;
    private static Camera? _startupCamera;
    private static string _startupUrl = string.Empty;
    private static float _startupAlpha;
    private static int _questLeaseReports;
    private static VideoPlayer? _failedPlayer;
    private static string _failedUrl = string.Empty;

    // The studio decoder can latch its plane state before a late render callback.
    // Its real replacement surface therefore reserves zero BEFORE native Play and
    // keeps it across frames. Desktop ambient movies keep their scoped draw lease;
    // Android qualifies the same persistent lifetime for its owned ambient consumer.
    internal static void ReserveStartup(VideoPlayer player)
    {
        ReleaseStartup();
        _failedPlayer = null; _failedUrl = string.Empty;
        _startupPlayer = player; _startupCamera = player.targetCamera;
        _startupUrl = player.url; _startupAlpha = player.targetCameraAlpha;
        player.errorReceived += StartupError;
        player.targetCameraAlpha = 0f;
        ReportQuestLease(player, "reserved");
    }
    private static void StartupError(VideoPlayer player, string message)
    {
        if (ReferenceEquals(player, _startupPlayer))
        {
            _failedPlayer = player; _failedUrl = player.url;
            ReleaseStartup();
        }
    }
    internal static void RefreshStartup()
    {
        if (ReferenceEquals(_startupPlayer, null)) return;
        VideoPlayer player = _startupPlayer!;
        if (player == null || _startupCamera == null || !player.isActiveAndEnabled
            || player.targetCamera != _startupCamera || player.url != _startupUrl
            || !(FlatScreen.OwnsStartupMovie(player)
                || QuestStandalonePlatform.Enabled && FlatScreen.OwnsCapturedMoviePlane(player))) { ReleaseStartup(); return; }
        if (player.targetCameraAlpha != 0f)
        {
            _startupAlpha = player.targetCameraAlpha; // Preserve a later native fade.
            player.targetCameraAlpha = 0f;
        }
    }
    // Android can retain the native camera-plane alpha before a render callback.
    // B711 records the packed studio decoder prepared with alpha one; its doubled
    // first logo and the smaller ambient strip match a surviving packed plane.
    // Do not rely on a zero borrowed only around a camera callback to suppress it.
    // Keep zero only while the exact decoder, camera, target and consuming surface
    // are owned. This also reacquires after an ordinary screen Hide/Show; a startup
    // prefix is not a lifetime lease when the screen can be temporarily suppressed.
    internal static void RefreshQuestMovie()
    {
        if (!QuestStandalonePlatform.Enabled) return;
        RefreshStartup();
        VideoPlayer? player = NativeVideoMovies.CameraPlaneSource;
        if (player == null || !FlatScreen.OwnsCapturedMoviePlane(player)
            || ReferenceEquals(player, _failedPlayer) && player.url == _failedUrl) { ReleaseStartup(); return; }
        if (!ReferenceEquals(player, _startupPlayer)) ReserveStartup(player);
    }

    private static void ReleaseStartup()
    {
        VideoPlayer? player = _startupPlayer;
        _startupPlayer = null; _startupCamera = null; _startupUrl = string.Empty;
        if (player == null) return;
        player.errorReceived -= StartupError;
        if (player.targetCameraAlpha == 0f) player.targetCameraAlpha = _startupAlpha;
        ReportQuestLease(player, "released");
    }
    private static void ReportQuestLease(VideoPlayer player, string action)
    {
        if (!QuestStandalonePlatform.Enabled || !VRLog.WantsDebug || _questLeaseReports >= 12) return;
        _questLeaseReports++;
        Camera camera = player.targetCamera;
        UnityEngine.Debug.Log("[Quest startup] native movie plane lease action=" + action
            + " key=" + NativeVideoMovies.NativePlaybackKey(player) + " frame=" + Time.frameCount
            + " planeAlpha=" + player.targetCameraAlpha + " presentationAlpha=" + _startupAlpha
            + " camera=" + (camera != null ? camera.name : "none")
            + " capture=" + (camera != null && camera.targetTexture != null ? camera.targetTexture.name : "none"));
    }
    internal static float PresentationAlpha(VideoPlayer player)
    {
        RefreshStartup();
        if (ReferenceEquals(player, _startupPlayer)) return _startupAlpha;
        float alpha = player.targetCameraAlpha;
        if (alpha != 0f) return alpha; // Preserve a later native/foreign fade write.
        // Stack enumeration visits inner to outer. The last matching lease is the
        // real alpha before any nested render borrowed zero from this player.
        foreach (Lease lease in Leases)
            if (ReferenceEquals(lease.Player, player)) alpha = lease.Alpha;
        return alpha;
    }
    internal static void BeforeRender(Camera camera)
    {
        if (_frame != Time.frameCount) { RestoreDraws(); _frame = Time.frameCount; }
        RefreshStartup();
        VideoPlayer? player = NativeVideoMovies.CameraPlaneSource;
        if (player == null || player.targetCamera != camera) return;
        if (QuestStandalonePlatform.Enabled) { RefreshQuestMovie(); return; }
        Leases.Push(new Lease(camera, player));
        player.targetCameraAlpha = 0f;
    }
    internal static void AfterRender(Camera camera)
    {
        if (Leases.Count > 0 && Leases.Peek().Camera == camera) Leases.Pop().Restore();
    }
    internal static void RestoreAll()
    {
        RestoreDraws();
        ReleaseStartup();
    }
    private static void RestoreDraws()
    {
        while (Leases.Count > 0) Leases.Pop().Restore();
    }
}
