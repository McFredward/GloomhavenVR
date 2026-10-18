using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Native game state is the only seam. Images, CanvasRenderer bindings, shaders,
// properties, texture identity, culling and active/color state are real Unity.
public sealed class CardEffects : MonoBehaviour
{
    public enum FXTask { BurnCard, LostMode }
    public Image? _uiFxOverlay;
    public Texture? overlayFrameBurn;
    public bool initialized = true;
    public int NativeReads;
    public readonly HashSet<FXTask> Effects = new();
    public bool HasEffect(FXTask effect) { NativeReads++; return Effects.Contains(effect); }
}
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform
    {
        internal static bool Enabled = true;
        public static bool DebugLogging => Enabled && VRLog.Wants(VRLogLevel.Debug);
    }
    internal enum VRLogLevel { Debug }
    internal static class VRLog
    {
        internal static bool DebugEnabled = true;
        internal static bool Wants(VRLogLevel level) => DebugEnabled;
    }
}
