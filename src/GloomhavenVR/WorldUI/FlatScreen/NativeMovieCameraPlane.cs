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
    internal static void BeforeRender(Camera camera)
    {
        if (_frame != Time.frameCount) { RestoreAll(); _frame = Time.frameCount; }
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
        while (Leases.Count > 0) Leases.Pop().Restore();
    }
}
