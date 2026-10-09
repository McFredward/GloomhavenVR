using System.Collections.Generic;
using UnityEngine;

// Explicit native/session/config/sky boundaries. Production Tick, clear writes,
// camera restoration and HDR owner are compiled verbatim and run in real Unity.
namespace GloomhavenVR.Core;
internal sealed class Entry<T> { internal T Value; internal Entry(T value) => Value = value; }
internal static class VRSession { internal static bool IsRunning = true; }
internal static class VRCameraPolicy
{
    internal static Camera? AllowedHead;
    internal static Camera[] Others = new Camera[0];
    internal static int GetAllCamerasNonAlloc(out Camera[] all) { all = Others; return all.Length; }
}
internal static class FrameNativePassthrough
{
    internal static bool Required, Ready, IsActive;
    internal static int Requests, Exits;
    internal static bool TryEnter() { Requests++; IsActive = Ready; return Ready; }
    internal static void Exit() { Exits++; IsActive = false; }
}
internal static class ElementMood { internal static void Tick() { } }
internal static class SkyAlternative
{
    internal static bool Tick() => false;
    internal static void StandDown() { }
}
internal static class SkyBackdrop { internal static void Tick(bool skyOwnedElsewhere) { } }
internal static class VRLog { internal static void Info(string module, string text) { } }
internal static partial class MixedReality
{
    private static readonly object _file = new();
    internal static readonly Entry<bool> Enabled = new(false);
    internal static readonly Entry<Color> KeyColor = new(Color.green);
    private static bool _active, _loggedActive, _skyboxSaved;
    private static Color _loggedColor;
    private static Material? _savedSkybox;
    private static readonly Dictionary<Camera, (CameraClearFlags Flags, Color Bg)> CamOriginals = new();
    private static readonly List<Renderer> HiddenSky = new();
    private static readonly List<Renderer> UnseenUnderlays = new();
    internal static Renderer? SkyFixture;
    private static string KeyColorName => "fixture green";
    private static void Bind() { }
    private static void HideSkyGeometry()
    {
        if (SkyFixture != null && SkyFixture.enabled)
        { HiddenSky.Add(SkyFixture); SkyFixture.enabled = false; }
    }
    private static void RestoreSky()
    {
        foreach (Renderer renderer in HiddenSky) if (renderer != null) renderer.enabled = true;
        HiddenSky.Clear();
    }
    private static void RestoreUnseenUnderlays() => UnseenUnderlays.Clear();
    private static void RetireSceneryBackings() => RestoreUnseenUnderlays();
}
