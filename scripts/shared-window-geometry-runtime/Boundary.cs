using System;
using UnityEngine;

namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static void Info(string area, string message) { }
        internal static void Note(string area, string message) { }
    }
}
namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver { internal static float BaseWorldScale = 12f; }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver
    {
        internal static bool Active;
        internal static float Scale = 198.12f;
        internal static bool TryGetParchmentFrame(out Vector3 center, out float scale)
        { center = Vector3.zero; scale = Scale; return Active; }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal enum SharedWindowKind : byte { None, ScenarioStory, MapStory, QuestConfirm, Encounter, Video, RewardShowcase }
    // Native identity/session and unrelated chrome are explicit boundaries. Unity hierarchy,
    // RectTransform, CanvasScaler and every production pose/size writer below execute unmodified.
    internal sealed class FixtureWindow { internal SharedWindowKind Kind = SharedWindowKind.None; }
    internal static class SharedWindows
    {
        internal static bool Online = true;
        internal static SharedWindowKind KindOf(FixtureWindow? window) => window?.Kind ?? SharedWindowKind.None;
        internal static bool ParticipatesHere(SharedWindowKind kind) => Online && kind != SharedWindowKind.None;
    }
    internal static class PanelLayout { internal static float WorldScale = 12f; }
    internal sealed class Dial { internal float Value = 1f; }
    internal static class WorldUIConfig { internal static Dial CanvasScaleMm = new(); }
    internal sealed class ConvertedPanel
    {
        internal GameObject HostGo = null!;
        internal RectTransform HostRect = null!;
        internal RectTransform Target = null!;
        internal bool IsAlive => HostGo != null;
        internal bool RenderHidden = false, OwnerRenderHidden = false, RevealPending = false;
        internal Vector2 SharedDesignFrame;
        internal string SharedDesignSource = "";
        internal SharedWindowKind SharedDesignKind;
        internal bool SharedSpawnPoseReady = false;
        internal int PoseGapSamples, PoseGapOverOnePx;
        internal float PoseGapSumPx, PoseGapWorstPx;
    }
    internal static partial class SharedWindowSize
    {
        private const float MinDesignPx = 64f, StretchTolerancePx = 64f;
        private static readonly System.Collections.Generic.HashSet<string> ArmLogged = new();
        internal static void ServiceResize(SharedWindowKind kind, string name, Vector2 rect, float factor, bool held) { }
    }
    internal static class PanelGrabHandle { internal const float MinScale = .15f, MaxScale = 2f; }
    internal sealed class FixtureHandle { internal bool IsGrabbed; }
    internal sealed class FixtureTween
    {
        internal void Cancel() { }
        internal bool Advance(Transform frame) => false;
    }
    internal static class PanelPoseWatch
    {
        internal enum Writer { UserGrab, Placement }
        internal static void Announce(ConvertedPanel panel, Writer writer, string why) { }
    }
    internal interface IPanelGrabOwner { Transform? GrabRoot { get; } }
    internal sealed partial class GrabbableModal : IPanelGrabOwner
    {
        private ConvertedPanel _panel = null!;
        private float _extraScale = 1f, _spawnWorldScale = 1f, _spawnSharedWorldScale = 1f;
        private string _logName = "fixture";
        private Transform? _holder, _frame, _visual;
        private readonly FixtureTween _reFaceTween = new();
        private readonly FixtureHandle _handle = new();
        private bool _shared, _visualValid, _easing, _updatePosValid;
        private SharedWindowKind _sharedKind;
        private bool UserMoved;
        private Vector3 _visualPos, _updatePos;
        private Quaternion _visualRot = Quaternion.identity;
        private float _visualScale = 1f;
        private const float PoseGapThresholdPx = 1f;
        internal bool IsGrabbed => _handle.IsGrabbed;
        internal Transform Frame => _frame!;
        Transform? IPanelGrabOwner.GrabRoot => _frame;
        internal void Participation(bool active)
        { _shared = active; _sharedKind = active ? _panel.SharedDesignKind : SharedWindowKind.None; }
        internal void Carry(Vector3 position, Quaternion rotation, float factor)
        { _handle.IsGrabbed = true; _frame!.SetPositionAndRotation(position, rotation); _frame.localScale = Vector3.one * factor; }
        internal void Release() { _handle.IsGrabbed = false; }
        private void EnsureFrame()
        {
            if (_holder != null) return;
            _holder = new GameObject("actual Unity holder").transform;
            _frame = new GameObject("actual Unity frame").transform;
            _frame.SetParent(_holder, false);
            _visual = new GameObject("actual Unity visual").transform;
            _visual.SetParent(_holder, false);
        }
        private void AdvanceVisual(float factor) => PinVisualToFrame(factor);
        private void ThrottleDiagWhileMoving(Transform host) { }
        private void ServiceInkCapture(Rect rect) { }
        private void SyncBar(float halfHeight, float width, float scale, Rect rect, float unit) { }
        private void SeatBadge(Transform host) { }
    }
    internal static partial class CanvasConversion { internal static Camera? WorldCamera; }
    internal static partial class ModalFallback
    {
        private const float WindowDistanceMeters = 1.3f, MaxSpawnPitchDeg = 15f;
        private struct HeightDecision { }
        private struct SpawnAnchor
        {
            internal bool Valid;
            internal Vector3 HeadPos, HeadForward, RawPos;
            internal float Scale;
            internal int ArcSlot;
        }
        private sealed class WindowPanel
        {
            internal ConvertedPanel Panel = null!;
            internal GrabbableModal? Grab;
            internal SpawnAnchor SpawnAnchor;
            internal bool PoseRePlaceDone = false;
        }
        private static readonly System.Collections.Generic.List<WindowPanel> Converted = new();
        private static SpawnAnchor s_lastSpawnAnchor;
        // Board-floor readability is exercised by the existing placement fixtures. This boundary
        // deliberately has no table and models the original centre-at-eye-level clamp only.
        private static void ClampSpawnPose(Vector3 head, Vector3 forward, ref Vector3 pos,
            float scale, Vector2 half, float pitch, bool centreAtEyeLevel, out HeightDecision result)
        { result = default; if (centreAtEyeLevel) pos.y = head.y; }
        private static void NoteSharedAnchorSpent(SharedWindowKind kind, string why) { }
        internal static void Register(ConvertedPanel panel, GrabbableModal grab)
        { Converted.Add(new WindowPanel { Panel = panel, Grab = grab }); }
        internal static bool ComputeHost(ConvertedPanel panel, bool replay, out Vector3 position, out Quaternion rotation, out float scale)
            => ComputeSharedHostSpawn(panel, replay ? s_lastSpawnAnchor : (SpawnAnchor?)null,
                Vector2.one, out position, out rotation, out scale);
        internal static bool ReplayDisabled(ConvertedPanel panel)
        {
            foreach (WindowPanel wp in Converted)
                if (ReferenceEquals(wp.Panel, panel)) return !wp.SpawnAnchor.Valid && wp.PoseRePlaceDone;
            return false;
        }
    }
}
