// Explicit decode/socket/fragment ports. CapabilityIngress.cs supplies the ACTUAL
// transport RaiseReceived body, driver MsgExtras case and VersionGuard registry kernels.
// This fixture proves ingress routing, not Photon delivery or binary codec acceptance.
using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net
{
    internal struct PresenceState
    { internal bool HasModVersion; internal ushort ModBuild; internal string? ModVersionText; }

    internal static class IngressDecodePort
    {
        internal sealed class Decoded
        { internal int Type; internal bool Standalone; internal PresenceState Presence; internal byte[][]? Pages; }
        private static byte _next;
        private static readonly Dictionary<byte, Decoded> Values = new();
        internal static byte[] Packet(bool hasVersion, ushort build, bool standalone = false)
        {
            var bytes = new byte[16]; bytes[0] = ++_next;
            Values.Add(bytes[0], new Decoded { Type = NetProtocol.MsgExtras, Standalone = standalone,
                Presence = new() { HasModVersion = hasVersion, ModBuild = build, ModVersionText = "fixture" } });
            return bytes;
        }
        internal static byte[] Batch(params byte[][] pages)
        {
            var bytes = new byte[16]; bytes[0] = ++_next;
            Values.Add(bytes[0], new Decoded { Type = NetProtocol.MsgPresentationBatch, Pages = pages });
            return bytes;
        }
        internal static Decoded? Read(byte[] bytes, int length) => length == bytes.Length && length > 0
            && Values.TryGetValue(bytes[0], out var value) ? value : null;
        internal static void Reset() { _next = 0; Values.Clear(); }
    }
    internal static class NetPacket
    { internal static int PeekType(byte[] bytes, int length) => IngressDecodePort.Read(bytes, length)?.Type ?? -1; }
    internal static class PresenceSerializer
    {
        internal static bool TryRead(byte[] bytes, int length, out PresenceState state)
        { var decoded = IngressDecodePort.Read(bytes, length); state = decoded?.Presence ?? default; return decoded?.Type == NetProtocol.MsgExtras; }
    }
    internal static class ExtrasVersionAnnouncement
    {
        internal static bool TryRead(byte[] bytes, int length, out PresenceState version)
        { var decoded = IngressDecodePort.Read(bytes, length); version = decoded?.Presence ?? default;
            return decoded?.Standalone == true && version.HasModVersion; }
    }
    internal static class PresentationBatch
    {
        internal static bool TryRead(byte[] bytes, int length, out byte[][]? pages)
        { pages = IngressDecodePort.Read(bytes, length)?.Pages; return pages != null; }
    }
    internal sealed class ExtrasFragments
    {
        internal static int CompressedPayloadType(byte[] bytes, int length) => -1;
        internal static int NativeSlotStream(byte[] bytes, int length) => -1;
        internal byte[]? Accept(int sender, byte[] bytes, int length, double now) => throw new InvalidOperationException("Unexercised fragment decode port");
    }
    internal static class ScenarioResultsCodec { internal const int MessageType = 29, FragmentType = 30; }
    internal sealed class NativeUseBarSnapshot { internal int Address; }
    internal static class NativeUseBarPacket
    { internal static bool TryRead(byte[] bytes, int length, out NativeUseBarSnapshot? state) { state = null; return false; } }

    internal static partial class VersionGuard
    {
        private struct PeerInfo { internal bool HasExtras; internal ushort Build; internal string? Text; }
        private static readonly Dictionary<int, PeerInfo> Peers = new();
        internal static void ResetIngressFixture() => Peers.Clear();
    }
    internal sealed partial class FfsNetTransport
    {
        private int LocalPlayerId => FFSNet.PlayerRegistry.MyPlayer.PlayerID;
        private readonly ExtrasFragments _fragments = new(), _townFragments = new(), _resultsFragments = new(),
            _animationFragments = new(), _plumeFragments = new(), _boardFragments = new(), _itemAppearanceFragments = new(),
            _mapTooltipFragments = new(), _appearanceFragments = new(), _promptFragments = new();
        private readonly ExtrasFragments[] _nativeFragments = new ExtrasFragments[32];
        private int _receivedFragments, _completedSnapshots;
        internal event Action<int, byte[], int>? PacketReceived;
        internal void ReceiveFixture(int sender, byte[] bytes) => RaiseReceived(sender, bytes, bytes.Length);
    }
    internal sealed partial class NetAvatarDriver
    {
        private readonly Dictionary<int, PresenceState> _pendingExtras = new();
        internal int WorldApplications;
        internal bool LastParsed;
        internal int PendingCount => _pendingExtras.Count;
        private void ExtrasToWorld(ref PresenceState state) => ++WorldApplications;
    }
}
namespace GloomhavenVR.Net.TownServices
{
    internal static class TownServiceCodec
    {
        internal const int MessageType = 20, FragmentType = 23;
        internal static bool TryReadBundle(byte[] bytes, int length, out byte[][]? modules) { modules = null; return false; }
    }
}
