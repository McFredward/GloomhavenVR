using System;
using System.Collections;
using GloomhavenVR.Net.TownServices;

public static partial class MirrorProgram
{
    private static void DelayedCensusRace()
    {
        const int peer = 77;
        TownServiceFrame Frame(ulong sequence, uint session, bool manifest, bool visible = true)
            => new TownServiceFrame { Service = 1, Session = session, Sequence = sequence,
                Module = manifest ? TownServiceFrame.ManifestModule : (ushort)3,
                Template = manifest ? (ushort)0 : (ushort)1, Structure = manifest ? 0u : 1u,
                Visible = visible, Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f },
                Nodes = manifest ? Array.Empty<TownServiceNode>() : new[] { new TownServiceNode { Binding = 1 } } };
        void Deliver(TownServiceFrame frame)
        {
            byte[] packet = TownServiceCodec.Write(frame);
            Check(TownServiceMirror.Receive(peer, packet, packet.Length), "race frame accepted");
        }
        bool Has(string store, ulong sequence)
        {
            var peers = (IDictionary)typeof(TownServiceMirror).GetField(store, PrivateStatic)!.GetValue(null)!;
            if (!peers.Contains(peer)) return false;
            var modules = (IDictionary)peers[peer]!;
            return modules.Contains((ushort)3) && ((TownServiceFrame)modules[(ushort)3]!).Sequence == sequence;
        }
        try
        {
            Deliver(Frame(101, 9, false));
            Deliver(Frame(100, 9, true)); // An older census excludes the newly opened module.
            Check(Has("ReceivedBaselines", 101) && Has("Pending", 101), "delayed census preserves newer complete baseline");
            TownServiceFrame current = Frame(102, 9, true); current.Modules = new ushort[] { 3 }; Deliver(current);
            Check(Has("ReceivedBaselines", 101), "new census can use already arrived baseline");
            TownServiceSessionInfo live = TownServiceMirror.RemoteSessions[peer];
            live.SessionAge = 17; live.ReceivedTime = UnityEngine.Time.unscaledTime - 3;
            float previousAge = live.SessionAge + UnityEngine.Time.unscaledTime - live.ReceivedTime;
            Deliver(Frame(103, 9, false));
            Check(UnityEngine.Mathf.Abs(live.SessionAge + UnityEngine.Time.unscaledTime - live.ReceivedTime - previousAge) < .001f,
                "module heartbeat cannot rewind author session age");
            Check(UnityEngine.Mathf.Abs(live.LastSeenTime - UnityEngine.Time.unscaledTime) < .001f, "module heartbeat refreshes independent liveness");
            live.ReceivedTime = UnityEngine.Time.unscaledTime - 20;
            TownServiceMirror.TickRemote(_ => null);
            Check(live.Active, "frequent modules keep session live without moving old manifest clock anchor");
            Deliver(Frame(110, 9, true, false));
            Check(!Has("ReceivedBaselines", 103), "later close prunes old baseline");
            Deliver(Frame(121, 10, false));
            Deliver(Frame(120, 9, true, false)); // Old-session close overtakes a new-session baseline.
            Check(Has("ReceivedBaselines", 121) && Has("Pending", 121), "old close preserves newer reopened-session baseline");
            current = Frame(122, 10, true); current.Modules = new ushort[] { 3 }; Deliver(current);
            Check(Has("ReceivedBaselines", 121), "reopened census keeps its original dependency");
        }
        finally { TownServiceMirror.RemovePeer(peer); }
    }
}
