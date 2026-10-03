using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Bounded visitor readiness for the shared mage's offered-hand pose. The
/// maintainer explicitly made pre-drop guides local-only (2026-10-03): they never
/// elect a visual owner, hide another eligible visitor's guide or paint remote ink.
/// Historical shared-guide wire fields remain readable for protocol compatibility.</summary>
internal static class TownServiceSharedCue
{
    private struct Visitor
    {
        internal uint Session;
        internal bool Ready;
        internal float Received;
    }

    private static readonly Dictionary<int, Visitor> Visitors = new();
    private static readonly Dictionary<int, Visitor> MerchantVisitors = new();
    private const float FreshSeconds = 3f;
    internal static bool LocalReady { get; private set; }
    internal static float LocalStrength { get; private set; }
    internal static bool PublishedReady => false;
    internal static float PublishedStrength => 0f;
    internal static int PublishedGuideOwner => 0;
    private static float _localSampled = float.NegativeInfinity;

    internal static void SetLocal(bool ready, float strength)
    {
        LocalReady = ready;
        LocalStrength = ready ? Mathf.Clamp01(strength) : 0f;
        _localSampled = Time.unscaledTime;
    }

    // The fast receiver has already checked its sender clock and session before this call.
    internal static void ObserveVisitor(int peer, uint session, bool ready, float strength)
    {
        if (peer <= 0 || session == 0 || float.IsNaN(strength) || float.IsInfinity(strength)) return;
        if (!Visitors.ContainsKey(peer) && Visitors.Count >= 8) return;
        Visitors[peer] = new Visitor { Session = session, Ready = ready, Received = Time.unscaledTime };
    }

    // Merchant readiness uses the numeric motion lifetime, independent of a
    // private shop visit or lifted-stock module. The receiver validates that
    // lifetime before this call, including withdrawal and out-of-order packets.
    internal static void ObserveMerchantVisitor(int peer, uint session, bool ready)
    {
        if (peer <= 0 || session == 0) return;
        if (!MerchantVisitors.ContainsKey(peer) && MerchantVisitors.Count >= 8) return;
        MerchantVisitors[peer] = new Visitor { Session = session, Ready = ready, Received = Time.unscaledTime };
    }

    internal static bool HasReadyMerchantVisitor
    {
        get
        {
            if (StoryComposite.PointOfNoReturn) return false;
            if (TownServiceMirror.TransactionOwner(1) != 0) return true;
            foreach (Visitor visitor in MerchantVisitors.Values)
                if (visitor.Ready && Time.unscaledTime - visitor.Received <= FreshSeconds) return true;
            return false;
        }
    }

    /// <summary>Read the original visitor's readiness without depending on a rendered
    /// guide. Matching the fresh native visit prevents an old palm affordance from
    /// extending a hand after that visitor changed service or reopened its session.</summary>
    internal static bool HasReadyVisitor(byte service)
    {
        if (service != 3 || StoryComposite.PointOfNoReturn) return false;
        float now = Time.unscaledTime;
        foreach (var pair in Visitors)
        {
            Visitor visitor = pair.Value;
            if (!visitor.Ready || now - visitor.Received > FreshSeconds
                || !TownServiceMirror.RemoteSessions.TryGetValue(pair.Key, out TownServiceSessionInfo? session)
                || !session.Active || session.Service != service || session.Session != visitor.Session
                || now - session.LastSeenTime > NetProtocol.StaleTimeoutSeconds) continue;
            return true;
        }
        return false;
    }

    // Read legacy fields without reinstating their retired shared-guide election.
    internal static void ObserveShared(int peer, bool ready, float strength, int guideOwner) { }
    internal static void Forget(int peer) { Visitors.Remove(peer); MerchantVisitors.Remove(peer); }
    internal static void Tick()
    {
        if (Time.unscaledTime - _localSampled > FreshSeconds || StoryComposite.PointOfNoReturn)
            SetLocal(false, 0f);
    }

    // Compatibility consumers may still query this while loading an older snapshot.
    // Only a physical card can occupy the palm; browsing readiness cannot do so.
    internal static int GuideOwner => TownServiceMirror.TransactionOwner(3);
    internal static void PaintLocal(CanvasGroup? gate, Transform? zone) { }
    internal static void PaintRemote(CanvasGroup? gate, Transform? zone)
    {
        if (gate == null) return;
        gate.alpha = 0f; gate.interactable = gate.blocksRaycasts = false;
    }

    internal static void Reset()
    {
        Visitors.Clear(); MerchantVisitors.Clear();
        LocalReady = false; LocalStrength = 0f;
        _localSampled = float.NegativeInfinity;
    }
}
