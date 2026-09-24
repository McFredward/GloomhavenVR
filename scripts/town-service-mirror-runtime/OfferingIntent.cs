using System;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

public static partial class MirrorProgram
{
    private static void MerchantOfferingIntent()
    {
        TownServiceMirror.Shutdown();
        try
        {
            Transform shared = Go("Offering owner frame").transform;
            Transform zone = Go("Actual merchant offering overlay", shared).transform;
            zone.gameObject.AddComponent<CanvasGroup>().alpha = 0f; // Actual card has replaced its ghost.
            TownServiceMirror.RegisterTemplate(1, 1, zone, address: TownServiceMirror.MerchantOfferingAddress);
            TownServiceMirror.BeginSession(1, 991, shared, zone);
            TownServiceMirror.RegisterModule(10, 1, zone, address: TownServiceMirror.MerchantOfferingAddress);
            TownServiceFrame? offering = null, manifest = null;
            foreach (byte[] packet in Capture())
            {
                Check(TownServiceCodec.TryRead(packet, packet.Length, out TownServiceFrame? frame), "offering fixture packet decodes");
                if (frame!.Module == TownServiceFrame.ManifestModule) manifest = frame;
                else offering = frame;
            }
            Check(offering != null && manifest != null, "owner supplies original offering module and membership");
            ulong sequence = 1000;
            void Deliver(TownServiceFrame frame)
            {
                frame.Sequence = ++sequence;
                byte[] bytes = TownServiceCodec.Write(frame);
                Check(TownServiceMirror.Receive(2, bytes, bytes.Length), "authoritative offering header is accepted");
            }
            // A late-join module can precede the manifest. The observer's mesh/font
            // readiness cannot decide whether the shared resident accepts an offer.
            Deliver(offering!);
            Check(!TownServiceMirror.RemoteMerchantOffering, "offering cannot outlive its owner membership");
            Deliver(manifest!);
            Check(TownServiceMirror.RemoteMerchantOffering, "parked zero-alpha overlay keeps the shared palm open");
            byte[] stale = TownServiceCodec.Write(offering!);
            offering!.Visible = false; Deliver(offering);
            Check(!TownServiceMirror.RemoteMerchantOffering, "owner withdrawal closes the shared palm before any asset playback");
            Check(TownServiceMirror.Receive(2, stale, stale.Length), "reordered offering header is harmless");
            Check(!TownServiceMirror.RemoteMerchantOffering, "older active overlay cannot reopen the palm");
            offering.Visible = true; Deliver(offering);
            Check(TownServiceMirror.RemoteMerchantOffering, "reclaimed eligible held card can request the palm again");
            manifest!.Modules = Array.Empty<ushort>(); Deliver(manifest);
            Check(!TownServiceMirror.RemoteMerchantOffering, "removed original overlay releases palm membership");
            manifest.Modules = new ushort[] { 10 }; Deliver(manifest); Deliver(offering);
            Check(TownServiceMirror.RemoteMerchantOffering, "restored original offering resumes after fresh membership");
            manifest.SampleTime = offering.SampleTime + 4f; Deliver(manifest);
            Check(!TownServiceMirror.RemoteMerchantOffering, "other fresh modules cannot preserve stale offering intent");
            offering.SampleTime = manifest.SampleTime; Deliver(offering);
            Check(TownServiceMirror.RemoteMerchantOffering, "fresh original offering renews its bounded lifetime");
            offering.Session = 992; Deliver(offering);
            manifest.Session = 992; Deliver(manifest);
            Check(TownServiceMirror.RemoteMerchantOffering, "reopened owner session adopts its earlier arriving original overlay");
            manifest.Visible = false; manifest.Modules = Array.Empty<ushort>(); Deliver(manifest);
            Check(!TownServiceMirror.RemoteMerchantOffering, "closed native visit cannot leave the resident offering");
            manifest.Visible = true; manifest.Modules = new ushort[] { 10 }; Deliver(manifest); Deliver(offering);
            TownServiceMirror.RemovePeer(2);
            Check(!TownServiceMirror.RemoteMerchantOffering, "departed visitor releases shared offering immediately");
            offering.TemplateAddress = "merchant.zone|"; Deliver(offering); Deliver(manifest);
            Check(!TownServiceMirror.RemoteMerchantOffering, "ordinary purchase areas never substitute for offered hand intent");
            TownServiceMirror.ResetNetwork();
            offering.TemplateAddress = TownServiceMirror.MerchantOfferingAddress;
            offering.PublicCatalog = true; manifest.PublicCatalog = true;
            Deliver(offering); Deliver(manifest);
            Check(!TownServiceMirror.RemoteMerchantOffering, "public cabinet lane cannot request a visitor handoff");
        }
        finally { TownServiceMirror.Shutdown(); Baselines.Clear(); }
    }
}
