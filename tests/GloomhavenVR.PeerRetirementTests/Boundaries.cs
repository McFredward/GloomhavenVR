using System;
using System.Collections.Generic;

// This fixture executes the complete retirement/enable methods, Results forwarding
// callback and TickGuard. Unity objects,
// native/transport teardown and held-object owners are fault-injectable boundary ports;
// their internals and real network/session callbacks are not executed here.
internal static class CleanupPorts
{
    internal static readonly List<string> Calls = new();
    internal static readonly HashSet<string> Fail = new();
    internal static readonly HashSet<int> Figures = new(), Props = new();
    internal static Action<string>? After;
    internal static void Step(string name, Action? operation = null)
    {
        Calls.Add(name);
        if (Fail.Contains(name)) throw new InvalidOperationException("destroyed presentation: " + name);
        operation?.Invoke();
        After?.Invoke(name);
    }
    internal static void Reset()
    {
        Calls.Clear(); Fail.Clear(); Figures.Clear(); Props.Clear(); After = null;
        GloomhavenVR.Core.VRLog.Lines.Clear();
        GloomhavenVR.Core.Events.VREvents.ResetForFixture();
        GloomhavenVR.Net.ScenarioResultsPose.ResetForFixture();
        UnityEngine.Time.unscaledTime += 11f;
    }
}

namespace GloomhavenVR.Core.Events
{
    // Only payload transport is modeled; no native window lifecycle is executed.
    internal readonly struct WindowVisibilityEvent
    {
        internal readonly object Window;
        internal readonly bool Shown;
        internal WindowVisibilityEvent(object window, bool shown) { Window = window; Shown = shown; }
    }
    internal static class VREvents
    {
        internal static event Action<WindowVisibilityEvent>? WindowVisibility;
        internal static int HandlerCount => WindowVisibility?.GetInvocationList().Length ?? 0;
        internal static void Publish(object window, bool shown) => WindowVisibility?.Invoke(new WindowVisibilityEvent(window, shown));
        internal static void ResetForFixture() => WindowVisibility = null;
    }
}

namespace UnityEngine
{
    internal static class Time { internal static float unscaledTime; }
}

namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static readonly List<string> Lines = new();
        internal static void Error(string scope, string line) => Lines.Add(scope + ": " + line);
    }
    internal static class PerfMonitor
    {
        internal static bool StepsActive => false;
        internal static long BeginStep() => 0;
        internal static void EndStep(string name, long start) { }
    }
}

namespace GloomhavenVR.Net
{
    internal sealed partial class NetAvatarDriver
    {
        private static NetAvatarDriver? _instance;
        internal static NetAvatarDriver? Current => _instance;
        internal readonly Dictionary<int, RemoteAvatar> _avatars = new();
        internal readonly Dictionary<int, int> _pending = new(), _pendingExtras = new(),
            _pendingAnimations = new(), _createRetryAt = new(), _peerEnv = new();
        internal readonly HashSet<int> _rxRejectLogged = new();
        internal readonly List<int> _scratchIds = new();
        internal object _transport = new FfsNetTransport();
        internal readonly ResetPort _tuningPager = new(), _fanPresentationSent = new();
        internal readonly DecisionPort _decisionWait = new();
        internal int _delayedDecisionPlayer;
        internal float _nextDecisionWait;
        internal void Add(int id)
        {
            _avatars.Add(id, new RemoteAvatar(id));
            _pending[id] = _pendingExtras[id] = _pendingAnimations[id] = _createRetryAt[id] = _peerEnv[id] = id;
            CleanupPorts.Figures.Add(id); CleanupPorts.Props.Add(id);
        }
        internal void Retire(int id) => ForgetPeer(id);
        internal void RetireAll() => DestroyAllAvatars();
        internal void Disable() => OnDisable();
        internal void Enable() => OnEnable();
        private void Subscribe() => CleanupPorts.Step("subscribe");
        private void Unsubscribe() => CleanupPorts.Step("unsubscribe");
        private void ResetNativePresentation() => CleanupPorts.Step("native-reset");
        private void ForgetNativePresentation(int id) => CleanupPorts.Step("native:" + id);
    }
    internal static class ScenarioResultsPose
    {
        internal static int Observed;
        internal static object? Window;
        internal static bool Shown;
        internal static void ObserveWindowVisibility(object window, bool shown)
        { Observed++; Window = window; Shown = shown; }
        internal static void ResetForFixture() { Observed = 0; Window = null; Shown = false; }
    }
    internal sealed class ResetPort { internal void Reset() { } }
    internal sealed class DecisionPort { internal void Tick(int player, int context, float time) { } }
    internal sealed class FfsNetTransport
    {
        internal void ForgetPeer(int id) => CleanupPorts.Step("transport:" + id);
        internal void ResetFragments() => CleanupPorts.Step("fragments");
    }
    internal sealed class RemoteAvatar
    {
        internal int Id { get; }
        internal RemoteAvatar(int id) => Id = id;
        internal void Destroy() => CleanupPorts.Step("avatar:" + Id);
    }
    internal static class NetFigures
    {
        internal static void ReleaseRemote(int id) => CleanupPorts.Step("figures:" + id, () => CleanupPorts.Figures.Remove(id));
    }
    internal static class NetProps
    {
        internal static void ReleaseRemote(int id) => CleanupPorts.Step("props:" + id, () => CleanupPorts.Props.Remove(id));
        internal static void Clear() => CleanupPorts.Step("props-clear", CleanupPorts.Props.Clear);
    }
    internal static class NetPlayerActors { internal static void ForgetAvatarFetch(int id) => CleanupPorts.Step("fetch:" + id); }
    internal static class RemoteTestTriggers { internal static void ForgetPeer(int id) => CleanupPorts.Step("triggers:" + id); }
    internal static class PeerCardFaceCensus { internal static void ReportPeerGone(int id) => CleanupPorts.Step("census:" + id); }
    internal static class CharacterDecisionPresentation { internal static void Remove(RemoteAvatar avatar) => CleanupPorts.Step("decision:" + avatar.Id); }
    internal static class NetCardFx { internal static void Reset() => CleanupPorts.Step("cardfx-reset"); }
    internal static class RemoteStorySync { internal static void Reset() => CleanupPorts.Step("story-reset"); }
    internal static class RemoteMapRoom { internal static void Reset() => CleanupPorts.Step("maproom-reset"); }
    internal static class RemoteMapStory { internal static void Reset() => CleanupPorts.Step("mapstory-reset"); }
    internal static class MapStoryLifecycle { internal static void Reset() => CleanupPorts.Step("lifecycle-reset"); }
    internal static class RemoteVideoPlayback { internal static void Reset() => CleanupPorts.Step("video-reset"); }
    internal static class VersionGuard { internal static void Reset() => CleanupPorts.Step("version-reset"); }
    internal static class PlayerBadges { internal static void RestoreAll() => CleanupPorts.Step("badges-reset"); }
    internal static class NetProtocol { internal const int HeldStretchCodeNeutral = 0; }
}

namespace GloomhavenVR.WorldUI.Surfaces
{
    internal static class DamageDecisionPreview
    {
        internal static void ResetRemote(GloomhavenVR.Net.RemoteAvatar avatar) => CleanupPorts.Step("preview:" + avatar.Id);
    }
}
namespace GloomhavenVR.Board
{
    internal static class CharacterFocus
    {
        internal static void ForgetPeer(int id) => CleanupPorts.Step("focus:" + id);
        internal static void Reset() => CleanupPorts.Step("focus-reset");
    }
}
namespace GloomhavenVR.Cards
{
    internal static class BoardCapPress { internal static void Clear() { } }
}
