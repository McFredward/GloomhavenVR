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
                if (!Sessions.TryGetValue(pair.Key, out TownServiceSessionInfo? session)
                    || !session.Active || session.Service != 1 || session.Session != intent.Session
                    || !intent.Visible || Array.BinarySearch(session.Modules, intent.Module) < 0
                    || now - session.LastSeenTime > OfferingFreshSeconds) continue;
                // Both sample times come from this same owner. Refreshing unrelated
                // inventory modules must not keep an old palm request alive forever.
                float age = now - session.ReceivedTime + session.SampleTime - intent.SampleTime;
                if (age <= OfferingFreshSeconds) return true;
            }
            return false;
        }
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
