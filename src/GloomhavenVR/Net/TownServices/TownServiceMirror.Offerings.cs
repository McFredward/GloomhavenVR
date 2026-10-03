using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    // This unique original overlay is active exactly while its owner requests the
    // merchant's palm, including a parked card whose ghost has zero CanvasGroup alpha.
    // No card identity, local viewer distance or copied UI callback decides the pose.
    internal const string MerchantOfferingAddress = "merchant.offering|";
    private const float OfferingFreshSeconds = 3f;
    private static readonly Dictionary<int, TownServiceFrame> MerchantOfferings = new();

    internal static bool RemoteMerchantOffering
    {
        get
        {
            float now = Time.unscaledTime;
            foreach (var pair in MerchantOfferings)
            {
                TownServiceFrame intent = pair.Value;
                if (InteractionOwner(1) != pair.Key
                    || !Sessions.TryGetValue(pair.Key, out TownServiceSessionInfo? session)
                    || !session.Active || session.Service != 1 || session.Session != intent.Session
                    || Array.BinarySearch(session.Modules, intent.Module) < 0
                    || now - session.LastSeenTime > OfferingFreshSeconds) continue;
                // Both sample times come from this same owner. Refreshing unrelated
                // inventory modules must not keep an old palm request alive forever.
                float age = now - session.ReceivedTime + session.SampleTime - intent.SampleTime;
                bool visible = intent.Visible;
                if (TryMerchantOfferingMotion(pair.Key, intent, now, out bool movedVisible, out float movedAge))
                { visible = movedVisible; age = movedAge; }
                if (visible && age <= OfferingFreshSeconds) return true;
            }
            return false;
        }
    }

    private static bool TryMerchantOfferingMotion(int peer, TownServiceFrame intent, float now,
        out bool visible, out float age)
    {
        visible = false; age = 0f;
        // The immutable original address/membership identifies this exact owner's
        // request even while its template assets are still initializing. A numeric
        // heartbeat refreshes only that request; unrelated inventory traffic cannot
        // keep the offered palm alive. Explicit withdrawal wins immediately.
        var key = new TownServiceMotionKey(1, 0, intent.Module, 0, 0, 0);
        if (!MotionPeers.TryGetValue(peer, out PeerMotion? source)
            || !source.Slots.TryGetValue(key, out MotionSlot? slot)
            || slot.Entry.Session != intent.Session || slot.Entry.Service != intent.Service
            || slot.Entry.Structure != intent.Structure || slot.Entry.PublicClaim != 0
            || slot.SampleTime < intent.SampleTime || now - slot.ReceivedAt > OfferingFreshSeconds) return false;
        visible = slot.Entry.Visible; age = Mathf.Max(0f, now - slot.ReceivedAt);
        return true;
    }

    private static void ObserveMerchantOffering(int peer, TownServiceFrame frame)
    {
        if (peer <= 0 || frame.Service != 1 || frame.TemplateAddress != MerchantOfferingAddress
            || !Sessions.TryGetValue(peer, out TownServiceSessionInfo? session)
            || !session.Active || session.Service != 1 || session.Session != frame.Session
            || Array.BinarySearch(session.Modules, frame.Module) < 0) return;
        if (MerchantOfferings.TryGetValue(peer, out TownServiceFrame? old)
            && old.Session == frame.Session && old.Sequence >= frame.Sequence) return;
        MerchantOfferings[peer] = frame;
    }

    private static void ReconcileMerchantOffering(int peer)
    {
        if (peer <= 0) return;
        MerchantOfferings.Remove(peer);
        // A module is allowed to arrive before its manifest, including late join.
        // Reconstruct only this owner's current membership; no template load is needed.
        if (Pending.TryGetValue(peer, out Dictionary<ushort, TownServiceFrame>? frames))
            foreach (TownServiceFrame frame in frames.Values) ObserveMerchantOffering(peer, frame);
    }
}
