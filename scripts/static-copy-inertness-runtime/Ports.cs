// Native bootstrap, final conversion and unchanged portrait detachment are explicit ports.
// The original native component types, singleton fields, UIWindow registry, RequireComponent
// graph and Unity activation/destruction are real; neither strip/admission decision is ported.
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
namespace GloomhavenVR.Core {
    internal static class VRLog {
        internal static bool WantsDebug => false;
        internal static int Refusals;
        internal static void Info(string scope,string text) { }
        internal static void Note(string scope,string text) { }
        internal static void Alert(string scope,string text) { Refusals++; }
        internal static void Warn(string scope,string text) { Refusals++; }
    }
    internal static class PerfMonitor { internal static IDisposable? Scope(string name) => null; }
}
namespace GloomhavenVR.Hands { internal enum HandSide { Left, Right } }
namespace GloomhavenVR.WorldUI {
    internal static class WorldUIConfig { internal static bool ConversionActive = true; }
    internal sealed class ConvertedPanel { }
    internal static class CanvasConversion { internal static void Release(ConvertedPanel panel) { } }
}
namespace GloomhavenVR.WorldUI.Surfaces {
    using GloomhavenVR.Core;
    using GloomhavenVR.Hands;
    internal static class FigureGrabConfig { internal static bool HeldFigureInfoEnabled = true; }
    internal enum HeldPropCardWindow { None, TextInfo, PropInfo }
    internal sealed class Dock { internal object? Route; }
    internal sealed partial class StatPanelSurface {
        private static GameObject? _copyHolder;
        private static RectTransform? _copyRect;
        private static ConvertedPanel? _copyPanel;
        private static ScenarioRuleLibrary.CActor? _copyActor;
        private static readonly List<Sprite> _copyOwnedSprites = new();
        private static readonly List<Texture2D> _copyOwnedTextures = new();
        private static long _held2Sequence = 1;
        private static long _refusedCopySequence;
        private static ActorStatPanel? _refusedCopySource;
        private static ScenarioRuleLibrary.CActor? _refusedCopyActor;
        // These existing methods do not change in this repair. This fixture uses no native
        // addressable portrait: its images/material/font must keep their exact source identities.
        private static int DetachSharedImagery(ActorStatPanel real, GameObject copy) => 0;
        private static string StructuralDiff(GameObject source, GameObject copy) => "fixture checks exact geometry";
        internal static RectTransform? Snapshot(ActorStatPanel real) { BuildStaticCopy(real, null!); return _copyRect; }
        internal static void NewHold() { _held2Sequence++; }
        internal static void Reset() { DestroyCopy(); _refusedCopySource=null;_refusedCopyActor=null; }
    }
    internal sealed partial class PropInfoSurface {
        private static GameObject? _copyHolder;
        private static RectTransform? _copyRect;
        private static ConvertedPanel? _copyPanel;
        private static HeldPropCardWindow _copyWindow;
        private static HandSide _copySide;
        private static string _copyLabel = string.Empty;
        private static bool _copyCommitted;
        private static Component? _refusedCopySource;
        private static HeldPropCardWindow _refusedCopyWindow;
        private static HandSide _refusedCopySide;
        private static string _refusedCopyLabel = string.Empty;
        private static readonly Dock _copyDock = new();
        private static string WindowName(HeldPropCardWindow window) => window.ToString();
        internal static RectTransform? Snapshot(string label) {
            SnapshotDisplacedCard(HeldPropCardWindow.TextInfo,HandSide.Left,label);return _copyRect;
        }
    }
}
public sealed class SnapshotProbe : MonoBehaviour {
    public static int Awakes;
    private void Awake() { Awakes++; }
}
public static class SnapshotFault { public static bool RefuseProbe; }
