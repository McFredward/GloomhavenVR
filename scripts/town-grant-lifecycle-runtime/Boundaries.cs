using System.Collections.Generic;
using GloomhavenVR.Net.TownServices;

// Native connectivity, player identity, clock and reliable delivery are the
// explicit boundaries. GrantSync/ledger/codec below are compiled from production.
namespace UnityEngine { internal static class Time { internal static float unscaledTime; } }
namespace FFSNet { internal static class FFSNetwork { internal static bool IsOnline = true; } }
namespace GloomhavenVR.Core { internal static class VRLog { internal static int CoordinatorReports; internal static void Warn(string category, string text) { } internal static void Note(string category, string text) { CoordinatorReports++; } } }
namespace GloomhavenVR.Net
{
    internal interface INetTransport { int LocalPlayerId { get; } bool IsOnline { get; } }
    internal sealed class FfsNetTransport : INetTransport
    {
        internal int Peer = 22;
        public int LocalPlayerId => Peer;
        public bool IsOnline => true;
        internal readonly List<TownGrantMessage> Sent = new();
        internal readonly List<byte[]> Packets = new();
        internal bool SendTownGrant(byte[] bytes, int length, bool hostOnly)
        {
            if (!TownServiceGrantCodec.TryRead(bytes, length, out var message)) return false;
            Sent.Add(message);
            var packet = new byte[length + 1]; packet[0] = hostOnly ? (byte)1 : (byte)0;
            System.Buffer.BlockCopy(bytes, 0, packet, 1, length); Packets.Add(packet);
            return true;
        }
    }
    internal static class NetSession { internal static bool FlatNetMode; }
    internal static class NetPlayerActors { internal static int Peer = 22; internal static int LocalPlayerId() => Peer; }
    internal static class PlayerRegistry { internal static int HostPlayerID = 11; }
    internal static class VersionGuard
    {
        internal static readonly List<int> Roster = new() { 11, 22, 33 };
        internal static readonly HashSet<int> Compatible = new() { 11, 22, 33 };
        internal static int PeerBuild(int id) => Compatible.Contains(id) ? NetProtocol.ModBuild : 0;
        internal static void CollectContinuationPeers(List<int> into, int local)
        { into.Clear(); foreach (int id in Roster) if (id != local && Compatible.Contains(id)) into.Add(id); }
    }
    internal static class NetProtocol { internal const int ModBuild = 611; internal const byte MsgTownGrant = 25; }
}
