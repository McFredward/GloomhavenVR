using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// The native Bolt socket/reflection target, authority census and timing sink are
// explicit ports. Queueing, codecs, expansion, batching and retry run actual source.
namespace GloomhavenVR.Net
{
    internal static class SharedWindowSpawnAuthority { internal static int AuthorId = 1; }
    internal static class NetSession { internal static bool FlatNetMode = false; }
    internal sealed partial class ResultsIngress
    {
        private readonly FfsNetTransport _transport = new();
        internal FfsNetTransport Transport => _transport;
        internal void Publish(ScenarioResultsState state, float now)
        { ScenarioResultsPresentation.Current = state; TickResultsSend(now); }
        internal bool Receive(int sender, byte[] bytes) => QueueResults(sender, bytes, bytes.Length);
        internal ScenarioResultsState? Take(int sender)
        {
            if (!_pendingResults.TryGetValue(sender, out var queue) || queue.Count == 0) return null;
            ScenarioResultsState result = queue[0]; queue.Clear(); return result;
        }
    }
    internal sealed partial class FfsNetTransport
    {
        private bool _degraded = false, _installed = true;
        internal bool IsOnline = true;
        internal int LocalPlayerId = 1;
        internal event Action<int, byte[], int>? PacketReceived;
        internal void Dispatch(int sender, byte[] bytes) => RaiseReceived(sender, bytes, bytes.Length);
        private readonly ExtrasSendScheduler _extrasQueue = new(10, 3, 4);
        private byte[]? _failedResultsPage, _versionAnnouncement;
        private float _resultsRetryBudget, _nextResultsRetryReport, _nextVersionAnnouncement = float.MaxValue, _nextFragmentReport, _nextTownGrantFailureLog;
        private int _sentFragments, _receivedFragments, _completedSnapshots, _fragmentBytes;
        private readonly object?[] _sendArgs = new object?[8];
        private readonly ConstructorInfo? _customDataCtor = typeof(NativeToken).GetConstructor(new[] { typeof(byte[]), typeof(bool) });
        private readonly MethodInfo? _sendSideAction = typeof(NativeSocket).GetMethod(nameof(NativeSocket.Send));
        internal void Enqueue(byte[] bytes, ScenarioResultsState state) => _extrasQueue.Enqueue(bytes, bytes.Length, identity: state);
        internal void Send(byte[] bytes, int length, ScenarioResultsState state) => _extrasQueue.Enqueue(bytes, length, identity: state);
        internal void EnqueueBackground(byte[] bytes, int slot = -1) => _extrasQueue.Enqueue(bytes, bytes.Length, slot);
        internal bool HasFailed => _failedResultsPage != null;
        internal bool EmissionFlagsRestored => _sendArgs[2] is true && _sendArgs[3] is false;
        internal void SetControlAvailability(bool installed, bool degraded)
        { _installed = installed; _degraded = degraded; }
        internal sealed class NativeToken
        {
            internal readonly byte[] Bytes;
            public NativeToken(byte[] bytes, bool compressed) { Bytes = bytes; }
        }
        internal static class NativeSocket
        {
            internal static readonly List<(byte[] Bytes, bool Unreliable)> Attempts = new();
            internal static readonly List<bool> HostRoutes = new();
            internal static bool FailNext;
            public static void Send(object? type, object token, bool unreliable, object? host,
                object? target, object? n1, object? n2, object? flag)
            {
                Attempts.Add((((NativeToken)token).Bytes, unreliable));
                HostRoutes.Add(host is true);
                if (FailNext) { FailNext = false; throw new InvalidOperationException("controlled native emission rejection"); }
            }
        }
    }
    // Native capture and peer discovery are explicit publisher ports. The exact
    // production TickResultsSend owns revision, basis, refresh and admission.
    internal sealed class ScenarioResultsPresentation
    {
        internal static ScenarioResultsState Current = new();
        internal void Reset() { }
        internal void Capture(ScenarioResultsState state)
        { state.Parts = Current.Parts; state.FrameSize = Current.FrameSize; }
    }
    internal static class ScenarioResultsPose
    {
        internal static uint Key => ScenarioResultsPresentation.Current.Key;
        internal static bool NativeVisible => ScenarioResultsPresentation.Current.Visible;
        internal static void BeginAuthorOpening() { }
        internal static bool Capture(ScenarioResultsState state)
        {
            var current = ScenarioResultsPresentation.Current;
            state.Key = current.Key; state.Author = current.Author; state.FrameSize = current.FrameSize;
            state.Pose = current.Pose; state.Size = current.Size; state.Reference = current.Reference;
            state.Origin = current.Origin; state.Rotation = current.Rotation; state.Scale = current.Scale;
            state.Move = current.Move; return true;
        }
    }
    internal static class VersionGuard
    {
        internal static void NotePacket(int sender) { }
        internal static void NoteExtras(int sender, in PresenceState state) { }
        internal static void CollectContinuationPeers(List<int> peers, int local)
        { peers.Clear(); peers.Add(2); peers.Add(3); }
    }
    internal static class SharedWindowMoves { internal static bool HasLocalClaim(SharedWindowKind kind) => false; }
    internal enum SharedWindowKind { Results }
    internal static class ScenarioResultsNative
    {
        internal static bool TryRead(out ScenarioResultsParts parts) { parts = default; return false; }
        internal static bool TryGetConverted(out ConvertedPanel? panel) { panel = null; return false; }
    }
    internal readonly struct ScenarioResultsParts { internal int PhaseBits => 0; }
    internal sealed class ConvertedPanel { internal RectTransform HostRect = null!; }
}
namespace GloomhavenVR.WorldUI
{
    internal static class MultiplayerCheats { internal static void NoteVersionReceipt(int sender, int build) { } }
}
namespace GloomhavenVR.Core
{
    internal static class ResultsClock { internal static float Now = 1f; }
    internal static class ResultsDiagnostics
    {
        internal static bool WantsProgress => true;
        internal static void Note(string scope, string message) { }
        internal static int Warnings;
        internal static void Error(string scope, string message) { }
        internal static void Warn(string scope, string message) { ++Warnings; }
    }
    internal static class PerfMonitor
    {
        internal readonly struct Timing : IDisposable { public void Dispose() { } }
        internal static Timing Scope(string name) => default;
    }
}
internal static class MyPluginInfo { internal const string PLUGIN_VERSION = "fixture"; }
