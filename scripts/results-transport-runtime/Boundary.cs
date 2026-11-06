using System;
using System.Collections.Generic;
using System.Reflection;

// The native Bolt socket/reflection target, authority census and timing sink are
// explicit ports. Queueing, codecs, expansion, batching and retry run actual source.
namespace GloomhavenVR.Net
{
    internal static class SharedWindowSpawnAuthority { internal static int AuthorId = 1; }
    internal static class NetSession { internal static bool FlatNetMode = false; }
    internal sealed partial class ResultsIngress
    {
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
        private readonly ExtrasSendScheduler _extrasQueue = new(10, 3, 4);
        private byte[]? _failedResultsPage, _versionAnnouncement;
        private float _resultsRetryBudget, _nextResultsRetryReport, _nextVersionAnnouncement = float.MaxValue, _nextFragmentReport;
        private int _sentFragments, _receivedFragments, _completedSnapshots, _fragmentBytes;
        private readonly object?[] _sendArgs = new object?[8];
        private readonly ConstructorInfo? _customDataCtor = typeof(NativeToken).GetConstructor(new[] { typeof(byte[]), typeof(bool) });
        private readonly MethodInfo? _sendSideAction = typeof(NativeSocket).GetMethod(nameof(NativeSocket.Send));
        internal void Enqueue(byte[] bytes, ScenarioResultsState state) => _extrasQueue.Enqueue(bytes, bytes.Length, identity: state);
        internal bool HasFailed => _failedResultsPage != null;
        internal sealed class NativeToken
        {
            internal readonly byte[] Bytes;
            public NativeToken(byte[] bytes, bool compressed) { Bytes = bytes; }
        }
        internal static class NativeSocket
        {
            internal static readonly List<(byte[] Bytes, bool Unreliable)> Attempts = new();
            internal static bool FailNext;
            public static void Send(object? type, object token, bool unreliable, object? host,
                object? target, object? n1, object? n2, object? flag)
            {
                Attempts.Add((((NativeToken)token).Bytes, unreliable));
                if (FailNext) { FailNext = false; throw new InvalidOperationException("controlled native emission rejection"); }
            }
        }
    }
}
namespace GloomhavenVR.Core
{
    internal static class ResultsDiagnostics { internal static void Error(string scope, string message) { } }
    internal static class PerfMonitor
    {
        internal readonly struct Timing : IDisposable { public void Dispose() { } }
        internal static Timing Scope(string name) => default;
    }
}
internal static class MyPluginInfo { internal const string PLUGIN_VERSION = "fixture"; }
