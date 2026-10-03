using System.Collections.Generic;
using GloomhavenVR.Net.TownServices;

// Native connectivity, player identity, clock and reliable delivery are the
// explicit boundaries. GrantSync/ledger/codec below are compiled from production.
namespace UnityEngine { internal static class Time { internal static float unscaledTime; } }
namespace FFSNet { internal static class FFSNetwork { internal static bool IsOnline = true; } }
namespace GloomhavenVR.Core { internal static class VRLog { internal static void Warn(string category, string text) { } } }
namespace GloomhavenVR.Net
{
    internal interface INetTransport { int LocalPlayerId { get; } bool IsOnline { get; } }
    internal sealed class FfsNetTransport : INetTransport
    {
        internal int Peer = 22;
        public int LocalPlayerId => Peer;
        public bool IsOnline => true;
        internal readonly List<TownGrantMessage> Sent = new();
        internal bool SendTownGrant(byte[] bytes, int length, bool hostOnly)
        { if (!TownServiceGrantCodec.TryRead(bytes, length, out var message)) return false; Sent.Add(message); return true; }
    }
    internal static class NetSession { internal static bool FlatNetMode; }
    internal static class NetPlayerActors { internal static int Peer = 22; internal static int LocalPlayerId() => Peer; }
    internal static class PlayerRegistry { internal static int HostPlayerID = 11; }
    internal static class VersionGuard { internal static int PeerBuild(int id) => NetProtocol.ModBuild; }
    internal static class NetProtocol { internal const int ModBuild = 611; internal const byte MsgTownGrant = 25; }
}
