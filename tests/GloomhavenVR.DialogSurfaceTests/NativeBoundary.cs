using System;
using UnityEngine;
using UnityEngine.UI;

internal static class Singleton<T> where T : class
{
    internal static T? Instance;
    internal static bool IsInitialized => Instance != null;
}
internal sealed class UIConfirmationBoxManager
{
    internal ConfirmationBox CurrentBox = new();
    internal event Action? BoxWindowShown, BoxWindowHidden;
    internal void Show(bool notify = true)
    {
        CurrentBox.IsOpen = true;
        if (notify) BoxWindowShown?.Invoke();
    }
    internal void Hide(bool notify = true)
    {
        // Native UIWindow emits onHidden before assigning its hidden state.
        if (notify) BoxWindowHidden?.Invoke();
        CurrentBox.IsOpen = false;
    }
    internal void Confirm(Action callback) { if (!CurrentBox.IsOpen) return; Hide(); callback(); }
    internal void Cancel(Action callback) { if (!CurrentBox.IsOpen) return; Hide(); callback(); }
}
internal sealed class ConfirmationBox
{
    internal bool IsOpen;
    internal readonly Transform transform = new RectTransform();
    internal readonly UIWindow Window = new();
    internal T? GetComponent<T>() where T : class => Window as T;
}
internal static class Choreographer { internal static object? s_Choreographer => null; }

namespace UnityEngine
{
    internal struct Vector3
    {
        public static Vector3 operator +(Vector3 a, Vector3 b) => a;
        public static Vector3 operator *(Vector3 a, float b) => a;
    }
    internal sealed class Camera { internal readonly Transform transform = new(); }
    internal class Transform { internal Vector3 position => default; internal Vector3 forward => default; }
    internal sealed class RectTransform : Transform { }
    internal struct Quaternion { }
}
namespace UnityEngine.UI
{
    internal sealed class UIWindow { }
    internal sealed class GraphicRaycaster { internal bool enabled = true; }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static int Warnings;
        internal static void Info(string scope, string value) { }
        internal static void Warn(string scope, string value) { ++Warnings; }
    }
}
namespace GloomhavenVR.Core.Events
{
    internal static class VRModeStateMachine { internal static bool TableInFrontOfPlayer = true; }
}
namespace GloomhavenVR.Hands.Interact
{
    internal static class PokeSurfaceTuning { internal const int SmallDialog = 1; }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class Flag { internal bool Value = true; }
    internal static class WorldUIConfig
    {
        internal static readonly Flag Dialogs = new();
        internal static bool ConversionActive = true;
    }
    internal static class FlatScreen { internal static bool ManualScreenActive; }
    internal static class PanelLayout { internal const float WorldScale = 1f; }
    internal sealed class ConvertedPanel
    {
        internal bool IsAlive = true;
        internal readonly GraphicRaycaster HostRaycaster = new();
    }
    internal static class CanvasConversion
    {
        internal static int Converts, Releases;
        internal static bool ReturnNull, ThrowConvert, ThrowPlace, ThrowRelease;
        internal static ConvertedPanel? Last;
        internal static Camera? WorldCamera = new();
        internal static ConvertedPanel? Convert(RectTransform? root, string name, int pokeTuning)
        {
            ++Converts;
            if (ThrowConvert) throw new InvalidOperationException("convert");
            if (ReturnNull) return null;
            Last = new ConvertedPanel(); return Last;
        }
        internal static void Release(ConvertedPanel panel)
        {
            if (ThrowRelease) throw new InvalidOperationException("release");
            ++Releases; panel.IsAlive = false;
        }
        internal static void PlaceHost(ConvertedPanel panel, Vector3 pos, Quaternion rotation, float scale)
        { if (ThrowPlace) throw new InvalidOperationException("placement"); }
        internal static void Reset()
        {
            Converts = Releases = 0; Last = null;
            ReturnNull = ThrowConvert = ThrowPlace = ThrowRelease = false;
            WorldCamera = new Camera();
        }
    }
    internal static class HeadFacing
    {
        internal struct Facing { internal Quaternion Rotation => default; }
        internal static Facing YawOnly(Transform head) => default;
        internal static void LogPlaced(string name, Facing facing, Vector3 pos, string reason) { }
    }
}
