using System;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    // A native caption/artwork heartbeat and a physical root are independent
    // owner clocks. Only the current private offered original may retain its
    // numeric root across a newer UI header. Withdrawal, remounting and native
    // return ownership continue to supersede it immediately.
    private static bool ContinuousOfferedRoot(int owner, RemoteModule module, MotionSlot sample, float now)
    {
        TownServiceMotionEntry entry = sample.Entry;
        TownServiceFrame? frame = module.LastFrame;
        if (frame == null || !module.Alive || sample.SampleTime < module.ReturnOriginChangedAt
            || entry.Kind != 1 || entry.Lane != 0 || entry.Hand != 0
            || !entry.Visible || entry.ParentAlpha <= 0f
            || sample.SampleTime < frame.SampleTime && (!frame.Visible || frame.ParentAlpha != entry.ParentAlpha)
            || frame.PublicCatalog || frame.VisitorStock || entry.PublicClaim != 0
            || entry.ParentModule != TownServiceFrame.ManifestModule || frame.ParentModule != entry.ParentModule
            || frame.ParentBinding != entry.Binding || frame.Session != entry.Session || frame.Service != entry.Service
            || frame.Module != entry.Module || frame.Structure != entry.Structure || frame.PublicClaim != entry.PublicClaim
            || !Sessions.TryGetValue(owner, out TownServiceSessionInfo? session) || !session.Active
            || session.Session != entry.Session || session.Service != entry.Service || session.PublicClaim != entry.PublicClaim
            || now - session.LastSeenTime > NetProtocol.StaleTimeoutSeconds
            || Array.BinarySearch(session.Modules, entry.Module) < 0) return false;

        bool offered = entry.Service == 3
            && (module.Address.StartsWith("face.", StringComparison.Ordinal)
                || module.Address.StartsWith("enchant.holder", StringComparison.Ordinal));
        // Stock originals and the independent wrist fan never acquire a palm
        // clock merely because their address contains an item ID. The current
        // private transaction lease is authored by the accepted native offer.
        if (entry.Service == 1)
            offered = session.TransactionActive && TransactionOwner(1) == owner
                && (module.Address.StartsWith("inspectionbody.", StringComparison.Ordinal)
                    || OfferedItemAddress(module.Address));
        if (!offered || PendingReturnModule(owner, module)) return false;
        // Do not replay an obsolete enclosing-canvas recipe over a new native
        // layout. Its world facing is part of this root clock; its rect, scale
        // and canvas settings remain the newer header's immediate authority.
        if (sample.SampleTime < frame.SampleTime && (entry.HasCanvasFrame != frame.HasCanvasFrame
            || entry.HasCanvasFrame && (!SameNumbers(entry.CanvasRect, frame.CanvasRect)
                || !SameNumbers(entry.CanvasSettings, frame.CanvasSettings)
                || entry.CanvasSortingLayer != frame.CanvasSortingLayer || entry.CanvasSortingOrder != frame.CanvasSortingOrder
                || Scale(entry.CanvasPose) != Scale(frame.CanvasPose)))) return false;
        if (MotionPeers.TryGetValue(owner, out PeerMotion? peer))
            for (byte kind = 7; kind <= 8; kind++)
                if (peer.Slots.TryGetValue(new TownServiceMotionKey(kind, entry.Lane, entry.Module, 0, 0, 0), out MotionSlot? flight)
                    && flight.Entry.Session == entry.Session && flight.Entry.Service == entry.Service
                    && flight.Entry.Structure == entry.Structure && flight.Entry.PublicClaim == entry.PublicClaim
                    && (kind == 8 ? LiveCardReturn(flight, now)
                        : flight.Entry.Numbers[0] + Mathf.Max(0f, now - flight.ReceivedAt) <= flight.Entry.Numbers[1] + .25f)) return false;
        return true;
    }

    private static bool OfferedItemAddress(string address)
    {
        if (!address.StartsWith("item.", StringComparison.Ordinal)) return false;
        int end = address.IndexOf('|');
        if (end != address.Length - 1 || end <= 5) return false;
        for (int i = 5; i < end; i++) if (address[i] < '0' || address[i] > '9') return false;
        return true;
    }
}
