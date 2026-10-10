using System.Collections.Generic;
using GloomhavenVR.Net.TownServices;

// Only native connectivity, identity, clock and reliable packet delivery are ports.
// Mirror election methods and the complete grant ledger/codec/sync are production.
namespace UnityEngine
{
    internal static class Time { internal static float unscaledTime; }
    internal static class Mathf
    {
        internal static float Max(float a, float b) => System.Math.Max(a, b);
        internal static float Abs(float a) => System.Math.Abs(a);
    }
}
namespace FFSNet { internal static class FFSNetwork { internal static bool IsOnline; } }
namespace GloomhavenVR.Core { internal static class VRLog { internal static void Warn(string category, string text) { } } }
namespace GloomhavenVR.Net
{
    internal interface INetTransport { int LocalPlayerId { get; } bool IsOnline { get; } }
    internal sealed class FfsNetTransport : INetTransport
    {
        internal int Peer;
        public int LocalPlayerId => Peer;
        public bool IsOnline => FFSNet.FFSNetwork.IsOnline;
        internal readonly List<TownGrantMessage> Sent = new();
        internal bool SendTownGrant(byte[] bytes, int length, bool hostOnly)
        { if (!TownServiceGrantCodec.TryRead(bytes, length, out var message)) return false; Sent.Add(message); return true; }
    }
    internal static class NetSession { internal static bool FlatNetMode; }
    internal static class NetPlayerActors { internal static int Peer; internal static int LocalPlayerId() => Peer; }
    internal static class PlayerRegistry { internal static int HostPlayerID = 11; }
    internal static class VersionGuard { internal static int PeerBuild(int id) => NetProtocol.ModBuild; }
    internal static class NetProtocol
    {
        internal const int ModBuild = 665;
        internal const byte MsgTownGrant = 25;
        internal const float StaleTimeoutSeconds = 3f;
    }
}
namespace GloomhavenVR.Net.TownServices
{
    internal sealed class TownServiceSessionInfo
    {
        internal bool Active, TransactionActive;
        internal byte Service;
        internal uint Session;
        internal float Started, LastSeenTime, SessionAge, ReceivedTime;
    }
    internal static partial class TownServiceMirror
    {
        internal static TownServiceSessionInfo PrivateLane = new();
        internal static readonly Dictionary<int, TownServiceSessionInfo> VisitorSessions = new();
        private static int LocalPeer => System.Math.Max(1, NetPlayerActors.LocalPlayerId());
        internal static void ResetFixture()
        { PrivateLane = new(); VisitorSessions.Clear(); ResetInteractionLeases(); }
    }
}
