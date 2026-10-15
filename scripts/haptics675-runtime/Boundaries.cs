using System;
using System.Collections.Generic;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Hands
{
    // Device delivery and clock are boundaries; production gate/sender bodies are bound unchanged.
    internal sealed partial class VRHand
    {
        internal static float Now;
        private static class Time { internal static float unscaledTime => Now; }
        private readonly HoverHapticGate _hoverHaptics = new();
        private float _lastHoverHapticTime = float.NegativeInfinity;
        private float _lastSemanticHoverHapticTime = float.NegativeInfinity;
        private const float HoverHapticMinInterval = .05f;
        private readonly object _device = new();
        internal readonly List<HapticPreset> Pulses = new();
        internal static VRHand? Active;
        internal bool HasPose = true;
        internal float WorldScale = 1;
        internal readonly RigBoundary Rig = new();
        internal readonly GrabberBoundary Grabber = new();
        internal readonly RayUguiBoundary RayUgui = new();
        internal sealed class RigBoundary { internal readonly Transform IndexTip = new GameObject("tip").transform; }
        internal sealed class GrabberBoundary { internal object? Held, Highlighted; }
        internal sealed class RayUguiBoundary { internal GameObject? Hovered; }
        internal void Hover(object? target) { Active=this; SendHoverHaptic(target); }
        internal void Legacy(HapticPreset preset) { Active=this; SendHaptic(preset); }
        internal void ClearPulses() => Pulses.Clear();
        internal void Destroy() => UnityEngine.Object.DestroyImmediate(Rig.IndexTip.gameObject);
    }
    internal static class VRHaptics { internal static void Play(object _, HapticPreset preset) => VRHand.Active!.Pulses.Add(preset); }
    internal enum HapticPreset { HoverTick, ClickPulse, GrabPulse }
    internal static class VRHands { internal static VRHand? Primary; }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static void Info(string area,string message) {} }
}
namespace GloomhavenVR.Cards
{
    internal sealed partial class VRCard : MonoBehaviour
    {
        internal bool CanGrab = true, IsHeld, PokeSelectEnabled = true, IsRooted;
        private bool _popped;
        internal bool AllowsHand(VRHand hand) => true;
        internal bool SetFingertipHover(bool value) { _popped = value; return true; }
        internal bool TryFingertipDistance(Vector3 point, out float distance)
        {
            var box=GetComponent<BoxCollider>();
            distance=Vector3.Distance(point,box.ClosestPoint(point));
            return box.enabled;
        }
    }
    internal sealed partial class CardFan
    {
        internal readonly List<VRCard> _cards = new();
        internal VRHand? _hand;
        internal bool IsOpen = true;
        private const float FingertipHoverReach=.035f;
        private VRCard? _pokeHoverCard;
        private static bool s_loggedFingertipHover;
        private int _pokeHoveredIndex=-1, _hoveredIndex=-1;
        private void Relayout(bool instant) {}
        internal void Frame() => UpdateFingertipHover();
    }
}
