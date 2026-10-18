using System;
using UnityEngine;
using UnityEngine.UI;

// Exact game namespace/conditional boundary: GH.Runtime has this GLOBAL type.
// Without ENABLE_LOGS, an unqualified Log call disappears at C# compilation.
// Keeping it in the Unity fixture prevents an editor-only successful logger
// binding from masking the real mod's missing Android records again.
public static class Debug
{
    [System.Diagnostics.Conditional("ENABLE_LOGS")]
    public static void Log(object message) => UnityEngine.Debug.Log(message);
    [System.Diagnostics.Conditional("ENABLE_LOGS")]
    public static void LogWarning(object message) => UnityEngine.Debug.LogWarning(message);
}

// Native object boundaries only. Discovery, source selection, sampling, quotas
// and state restoration execute the unchanged production observer in Unity.
public sealed class FullAbilityCard : MonoBehaviour { public Image headerImage = null!; }
public sealed class InitiativeTrack : MonoBehaviour { public static InitiativeTrack? Instance; }
public sealed class InitiativeTrackActorAvatar : MonoBehaviour { public Image selectionImage = null!; }
public sealed class ActorStatPanel : MonoBehaviour { public Image characterPortrait = null!; public Image enemyPortrait = null!; }
public static class Singleton<T> where T : class
{
    public static T Instance = null!;
    public static bool IsInitialized => Instance != null;
}
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled; internal static bool DebugLogging = true; internal static string ResourceDirectory = ""; }
    internal static class VRLog
    {
        internal static bool WantsDebug;
        internal static bool Wants(VRLogLevel level) => WantsDebug;
        internal static void Info(string scope, string text) => UnityEngine.Debug.Log(text);
        internal static void Warn(string scope, string text) => UnityEngine.Debug.LogWarning(text);
    }
    internal enum VRLogLevel { Debug }
    internal static class PerfMonitor
    {
        internal readonly struct Measure : IDisposable { public void Dispose() {} }
        internal static Measure Scope(string name) => new();
        internal static void Count(string name) {}
    }
}
namespace GloomhavenVR.Cards
{
    internal sealed class Setting<T> { internal T Value; internal Setting(T value) { Value = value; } }
    internal enum ControlBoard { Oak, Steel, Bronze }
    internal static class CardsConfig {
        internal static readonly Setting<bool> FaceMipBake = new(true);
        internal static readonly Setting<float> CardWidth = new(.0635f);
        internal static float CardHeight => .088f;
        internal static ControlBoard CurrentBoard = ControlBoard.Oak;
        internal static Setting<Vector3> SlotOverlayOffset(ControlBoard board) => new(new Vector3(.002f, -.003f, .001f));
        internal static Setting<float> SlotOverlaySpacing(ControlBoard board) => new(-.008f);
    }
    internal static class PlayTray { internal static Shader? OverlayShader() => Resources.Load<Shader>("PrimitiveOverlay"); }
    internal static class CardFan {}
    internal static class CardFace { internal static void Offer(Component root) {} internal static void MaintainArtArrival() {} }
    internal static class CardFxBounds { internal static void Reseat(Component root) {} }
}
namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig { internal static readonly GloomhavenVR.Cards.Setting<bool> PanelMipBake = new(true); }
    internal static class CanvasConversion {
        internal static Camera? WorldCamera;
        internal static int OrderAboveDistance(float distance, int lift) => lift;
    }
}

namespace GloomhavenVR.Cards
{
    // Mesh writes are the only boundary: production capture/qualification executes
    // unchanged. A refused mask must never reach this native/body mutation seam.
    internal enum CardBodyKind { Ability }
    internal static class CardMesh
    {
        internal static int Writes;
        internal static byte[]? LastAlpha;
        internal static bool SilhouetteApplied(CardBodyKind kind) => false;
        internal static void RefreshSilhouetteCache(CardBodyKind kind, byte[] alpha, int width, int height, Rect rect, string source) { Writes++; }
        internal static bool SetSilhouette(CardBodyKind kind, byte[] alpha, int width, int height, Rect rect, string source, bool fromCache) { Writes++; LastAlpha = alpha; return true; }
    }
    internal static class FaceBlackout { internal static void InvalidateRateLimits() {} }
}
