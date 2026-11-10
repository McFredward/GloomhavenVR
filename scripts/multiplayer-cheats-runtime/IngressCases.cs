using FFSNet;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;

internal static partial class Program
{
    private static void CapabilityIngress()
    {
        Reset(); IngressDecodePort.Reset(); VersionGuard.ResetIngressFixture();
        // A new native generation cannot reuse Reset's deliberately advertised old peer.
        PlayerRegistry.AllPlayers[1] = new() { PlayerID = 2 };
        var driver = new NetAvatarDriver(); var transport = new FfsNetTransport();
        transport.PacketReceived += driver.ReceiveExtrasFixture;
        Check(!NativeCheatQueue.HasCurrentVersion(2), "new ingress peer starts without an identity receipt");
        transport.ReceiveFixture(2, IngressDecodePort.Packet(true, NetProtocol.ModBuild, standalone: true));
        Check(NativeCheatQueue.HasCurrentVersion(2), "actual standalone version ingress grants current native identity receipt");
        Check(VersionGuard.PeerBuild(2) == NetProtocol.ModBuild && driver.WorldApplications == 0 && driver.PendingCount == 0,
            "actual standalone early return retains registry announcement without clearing presentation");
        transport.ReceiveFixture(2, IngressDecodePort.Packet(false, 0));
        Check(NativeCheatQueue.HasCurrentVersion(2), "actual sparse extras retain the prior explicit native identity receipt");
        Check(VersionGuard.PeerBuild(2) == NetProtocol.ModBuild && driver.WorldApplications == 1 && driver.LastParsed,
            "source-derived driver sparse extras still publish ordinary presentation");
        transport.ReceiveFixture(2, IngressDecodePort.Packet(true, (ushort)(NetProtocol.ModBuild - 1)));
        Check(!NativeCheatQueue.HasCurrentVersion(2) && VersionGuard.PeerBuild(2) == NetProtocol.ModBuild - 1,
            "explicit incompatible ordinary extras revoke native identity admission");
        transport.ReceiveFixture(2, IngressDecodePort.Packet(false, 0));
        Check(!NativeCheatQueue.HasCurrentVersion(2), "sparse extras cannot invent a fresh compatible receipt");
        transport.ReceiveFixture(2, IngressDecodePort.Packet(true, NetProtocol.ModBuild, standalone: true));
        Check(NativeCheatQueue.HasCurrentVersion(2), "standalone compatible advertisement restores admission");
        transport.ReceiveFixture(2, IngressDecodePort.Packet(true, 0, standalone: true));
        Check(!NativeCheatQueue.HasCurrentVersion(2) && VersionGuard.PeerBuild(2) == 0,
            "explicit incompatible standalone advertisement revokes admission too");
        transport.ReceiveFixture(2, IngressDecodePort.Packet(true, NetProtocol.ModBuild, standalone: true));
        PlayerRegistry.AllPlayers[1] = new() { PlayerID = 2 };
        Check(!NativeCheatQueue.HasCurrentVersion(2) && VersionGuard.PeerBuild(2) == NetProtocol.ModBuild,
            "a cached numeric version is not a reused native identity receipt");
        transport.ReceiveFixture(2, IngressDecodePort.Packet(false, 0));
        Check(!NativeCheatQueue.HasCurrentVersion(2), "sparse extras cannot bind a reused native id to an older advertisement");
        transport.ReceiveFixture(2, IngressDecodePort.Packet(true, NetProtocol.ModBuild));
        Check(NativeCheatQueue.HasCurrentVersion(2), "fresh ordinary version binds only the current replacement entity");
        ++VersionGuard.SessionEpoch;
        transport.ReceiveFixture(2, IngressDecodePort.Packet(false, 0));
        Check(!NativeCheatQueue.HasCurrentVersion(2), "session retirement survives versionless ingress without granting trust");
        transport.ReceiveFixture(2, IngressDecodePort.Batch(
            IngressDecodePort.Packet(true, NetProtocol.ModBuild, standalone: true), IngressDecodePort.Packet(false, 0)));
        Check(NativeCheatQueue.HasCurrentVersion(2) && VersionGuard.PeerBuild(2) == NetProtocol.ModBuild,
            "actual recursive transport batch routes version then sparse extras without erasing trust");
        int applications = driver.WorldApplications;
        transport.ReceiveFixture(1, IngressDecodePort.Packet(true, 0, standalone: true));
        Check(NativeCheatQueue.HasCurrentVersion(2) && driver.WorldApplications == applications,
            "actual transport local-sender exclusion stays intact");
        NetSession.FlatNetMode = true;
        transport.ReceiveFixture(2, IngressDecodePort.Packet(true, 0, standalone: true));
        Check(NativeCheatQueue.HasCurrentVersion(2) && driver.WorldApplications == applications,
            "actual transport flat-net exclusion stays intact");
        NetSession.FlatNetMode = false;
    }
}
