using System.Collections.Generic;
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

    // The studio decoder can latch its plane state before a late render callback.
    // Its real replacement surface therefore reserves zero BEFORE native Play and
    // keeps it across frames. Other movies retain their scoped camera-draw lease.
    internal static void ReserveStartup(VideoPlayer player)
    {
        ReleaseStartup();
        _startupPlayer = player; _startupCamera = player.targetCamera;
        _startupUrl = player.url; _startupAlpha = player.targetCameraAlpha;
        player.errorReceived += StartupError;
        player.targetCameraAlpha = 0f;
    }
    private static void StartupError(VideoPlayer player, string message)
    {
        if (ReferenceEquals(player, _startupPlayer)) ReleaseStartup();
    }
    internal static void RefreshStartup()
    {
        if (ReferenceEquals(_startupPlayer, null)) return;
        VideoPlayer player = _startupPlayer!;
        if (player == null || _startupCamera == null || !player.isActiveAndEnabled
            || player.targetCamera != _startupCamera || player.url != _startupUrl
            || !FlatScreen.OwnsStartupMovie(player)) { ReleaseStartup(); return; }
        if (player.targetCameraAlpha != 0f)
        {
            _startupAlpha = player.targetCameraAlpha; // Preserve a later native fade.
            player.targetCameraAlpha = 0f;
        }
    }
    private static void ReleaseStartup()
    {
        VideoPlayer? player = _startupPlayer;
        _startupPlayer = null; _startupCamera = null; _startupUrl = string.Empty;
        if (player == null) return;
        player.errorReceived -= StartupError;
        if (player.targetCameraAlpha == 0f) player.targetCameraAlpha = _startupAlpha;
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
