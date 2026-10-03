using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>One visual approach response for the explicitly shared enchantress guide.
/// Every visitor still evaluates its own native eligibility and controller haptics.
/// Only the elected resident author combines those responses for the common picture.</summary>
internal static class TownServiceSharedCue
{
    private struct Visitor
    {
        internal uint Session;
        internal bool Ready;
        internal float Strength, Received;
    }

    private static readonly Dictionary<int, Visitor> Visitors = new();
    private const float FreshSeconds = 3f;
    internal static bool LocalReady { get; private set; }
    internal static float LocalStrength { get; private set; }
    internal static bool PublishedReady { get; private set; }
    internal static float PublishedStrength { get; private set; }
    private static float _localSampled = float.NegativeInfinity;
    private static int _sharedAuthor;
    private static bool _sharedReady;
    private static float _sharedStrength;
    private static float _sharedReceived = float.NegativeInfinity;

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
        Visitors[peer] = new Visitor { Session = session, Ready = ready,
            Strength = ready ? Mathf.Clamp01(strength) : 0f, Received = Time.unscaledTime };
    }

    internal static void ObserveShared(int peer, bool ready, float strength)
    {
        if (peer <= 0 || peer != RemoteTownResidents.AuthorPlayer
            || float.IsNaN(strength) || float.IsInfinity(strength)) return;
        _sharedAuthor = peer; _sharedReady = ready;
        _sharedStrength = ready ? Mathf.Clamp01(strength) : 0f;
        _sharedReceived = Time.unscaledTime;
    }

    internal static void Forget(int peer)
    {
        Visitors.Remove(peer);
        if (_sharedAuthor != peer) return;
        _sharedAuthor = 0; _sharedReady = false; _sharedStrength = 0f;
        _sharedReceived = float.NegativeInfinity;
    }

    internal static void Tick()
    {
        float now = Time.unscaledTime;
        if (now - _localSampled > FreshSeconds) SetLocal(false, 0f);
        PublishedReady = false; PublishedStrength = 0f;
        if (!TownServicePopulation.IsFaceAuthor || StoryComposite.PointOfNoReturn) return;
        // A placed card owns the shared palm. Its owner's existing replacement cue
        // remains local/native; a different visitor must not paint a second offer.
        if (TownServiceGrantSync.GrantedOwner(3) != 0) return;
        PublishedReady = LocalReady;
        PublishedStrength = LocalStrength;
        foreach (Visitor visitor in Visitors.Values)
        {
            if (!visitor.Ready || now - visitor.Received > FreshSeconds) continue;
            PublishedReady = true;
            PublishedStrength = Mathf.Max(PublishedStrength, visitor.Strength);
        }
    }

    internal static void PaintLocal(CanvasGroup? gate, Transform? zone)
    {
        // Never hide an owner's native replacement affordance, or make an ineligible
        // local drop actionable. This changes only the already elected visible guide.
        if (gate == null || zone == null || gate.alpha <= .01f
            || !TownServiceMirror.CanShowLocalCue(3)) return;
        PaintShared(gate, zone);
    }

    internal static void PaintRemote(CanvasGroup? gate, Transform? zone)
    {
        if (gate == null || zone == null || gate.alpha <= .01f) return;
        PaintShared(gate, zone);
    }

    private static void PaintShared(CanvasGroup gate, Transform zone)
    {
        bool author = TownServicePopulation.IsFaceAuthor;
        bool ready = author ? PublishedReady : _sharedAuthor == RemoteTownResidents.AuthorPlayer
            && Time.unscaledTime - _sharedReceived <= FreshSeconds && _sharedReady;
        if (!ready) return;
        float strength = author ? PublishedStrength : _sharedStrength;
        // This is the same ink/scale response used by the existing local offer guide.
        TownServiceOfferFeedback.PaintInk(zone, zone.Find("Border")?.GetComponent<Image>(), strength);
    }

    internal static void Reset()
    {
        Visitors.Clear(); LocalReady = PublishedReady = _sharedReady = false;
        LocalStrength = PublishedStrength = _sharedStrength = 0f;
        _localSampled = _sharedReceived = float.NegativeInfinity; _sharedAuthor = 0;
    }
}
