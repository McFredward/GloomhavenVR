using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Net.TownServices;

internal sealed class TownServiceSessionInfo
{
    internal int Peer;
    internal uint PublicClaim;
    internal byte Service;
    internal uint Session;
    internal ulong Sequence;
    internal float SampleTime, ReceivedTime, LastSeenTime, SessionAge;
    internal bool Active;
    internal bool TempleDonationKnown, TempleDonationAvailable;
    internal bool HasTempleDonationCommitAge;
    internal uint TempleDonationRevision;
    internal float TempleDonationChangedTime;
    internal bool TransactionActive;
    internal Vector3 Position, Scale;
    internal Quaternion Rotation;
    internal ushort[] Modules = Array.Empty<ushort>();
}

internal readonly struct TownTempleDonationState
{
    internal readonly int Peer;
    internal readonly uint Session;
    internal readonly bool Known, Available;
    internal readonly uint Revision;
    internal readonly float TransitionAge;
    internal readonly bool HasCommitAge;
    internal TownTempleDonationState(int peer, uint session, bool known, bool available,
        uint revision, float transitionAge, bool hasCommitAge = false)
    { Peer = peer; Session = session; Known = known; Available = available;
      Revision = revision; TransitionAge = transitionAge; HasCommitAge = hasCommitAge; }
}

/// <summary>
/// Per-visitor original widget mirrors. Register original templates without opening a remote
/// gameplay service; register local modules after native conversion has completed. No callback,
/// model setter, purchase command or game controller runs on these observer copies.
/// </summary>
internal static partial class TownServiceMirror
{
    static TownServiceMirror()
    {
        TownServiceDelivery.Completed = SnapshotSent;
        TownServiceVoice.RelayRequest = QueueVoiceReaction;
        TownServiceVoice.StockRelayRequest = QueueStockVoiceReaction;
    }
    internal static readonly TownServiceAssets Assets = new();
    private static readonly Dictionary<int, TownServiceSessionInfo> Sessions = new();
    private static readonly Dictionary<int, TownServiceSessionInfo> VisitorSessions = new();
    internal static IReadOnlyDictionary<int, TownServiceSessionInfo> RemoteSessions => VisitorSessions;
    /// <summary>A remote visit alone does not prove that the enchantress's native card
    /// destination is ready. Author her offered-hand pose only after the original cue
    /// is visible, or while that visitor has actually parked a card.</summary>
    internal static bool HasVisibleRemoteEnhancementCue()
    {
        float now = Time.unscaledTime;
        foreach (var pair in VisitorSessions)
        {
            TownServiceSessionInfo visit = pair.Value;
            if (pair.Key <= 0 || !visit.Active || visit.Service != 3
                || now - visit.LastSeenTime > NetProtocol.StaleTimeoutSeconds) continue;
            if (!Remote.TryGetValue(pair.Key, out Dictionary<ushort, RemoteModule>? modules)) continue;
            foreach (RemoteModule module in modules.Values)
            {
                if (module.Session != visit.Session || !module.Host.activeInHierarchy) continue;
                CanvasGroup? parent = module.Host.GetComponent<CanvasGroup>();
                if (parent != null && parent.alpha <= .01f) continue;
                if (visit.TransactionActive && module.Address.StartsWith("face.", StringComparison.Ordinal))
                    return true;
                if (module.Address != "merchant.zone|") continue;
                CanvasGroup? cue = module.Binding.Root.GetComponent<CanvasGroup>();
                if (cue != null && cue.alpha > .01f) return true;
            }
        }
        return false;
    }
    internal static IReadOnlyDictionary<int, TownServiceSessionInfo> PublicSessions => Sessions;
    internal static event Action<string>? PresentationUnavailable;
    internal static Func<int, Transform?>? SharedFrameForRemote { get; set; }
    private static readonly Dictionary<string, GameObject> Templates = new(StringComparer.Ordinal);
    internal static Func<byte, ushort, string, bool>? ResolveTemplate { get; set; }
    // Presentation-only clone preparation: native gameplay controllers remain neutralized.
    // Generated physical bodies need their clone registered for later original silhouette updates.
    internal static Action<string, GameObject>? PrepareInertGeometry { get; set; }
    // Renderer-only finishing after native text, pose and bounds have been replayed.
    internal static Action<string, Transform, Transform>? FinishInertPresentation { get; set; }
    private static Dictionary<ushort, LocalModule> Local => _local.Modules;
    private static readonly Dictionary<int, Dictionary<ushort, RemoteModule>> Remote = new();
    private static readonly List<ushort> RetiredRemoteChildren = new();
    private static readonly Dictionary<int, Dictionary<ushort, TownServiceFrame>> Pending = new();
    private static readonly Dictionary<int, Dictionary<ushort, TownServiceFrame>> ReceivedBaselines = new();
    private static readonly Dictionary<long, float> RemoteRetry = new();
    private readonly struct ParentLink
    { internal readonly ushort Module; internal readonly uint Binding;
        internal ParentLink(ushort module, uint binding) { Module = module; Binding = binding; } }
    private static Dictionary<Transform, ParentLink> SourceParents => _local.Parents;
    private static readonly List<CanvasGroup> ParentGroups = new(2);
    private static readonly List<Canvas> ParentCanvases = new(4);
    private static GameObject? _templateHost;
    private static Transform? _sharedFrame { get => _local.SharedFrame; set => _local.SharedFrame = value; }
    private static Transform? _station { get => _local.Station; set => _local.Station = value; }
    private static byte _service { get => _local.Service; set => _local.Service = value; }
    private static uint _session { get => _local.Session; set => _local.Session = value; }
    private static ulong _sequence { get => _local.Sequence; set => _local.Sequence = value; }
    private static bool _active { get => _local.Active; set => _local.Active = value; }
    private static float _nextManifest { get => _local.NextManifest; set => _local.NextManifest = value; }
    private static float _closedUntil { get => _local.ClosedUntil; set => _local.ClosedUntil = value; }
    private static float _sessionStarted { get => _local.Started; set => _local.Started = value; }
    private static ushort _heartbeatModule { get => _local.Heartbeat; set => _local.Heartbeat = value; }
    private static readonly Dictionary<string, float> Failures = new(StringComparer.Ordinal);
    private static float _reportWindow;
    private static int _reportCount;

    private static uint _publicClaim, _observedPublicClaim;
    private static int LocalPeer => Math.Max(1, NetPlayerActors.LocalPlayerId());

    private sealed class InteractionLease
    {
        internal int Player;
        internal uint Session;
        internal float PendingSince = float.NegativeInfinity;
    }
    private static readonly InteractionLease[] InteractionLeases =
    {
        new(), new(), new(), new()
    };
    private static readonly InteractionLease[] TransactionLeases =
    {
        new(), new(), new(), new()
    };
    private const float InteractionClaimSettleSeconds = .12f;

    /// <summary>The single author of one resident's shared presentation. Every visitor
    /// may browse before placing an offer; this election only decides whose original
    /// modules are copied at the physical stand. A transaction claimant temporarily
    /// takes authorship so the offered card and confirmation have one shared source.
    /// Session close, walk-away, disconnect or timeout releases this visual lease.</summary>
    internal static int InteractionOwner(byte service)
    {
        if (service < 1 || service > 3) return 0;
        int transactionOwner = TransactionOwner(service);
        if (transactionOwner != 0) return transactionOwner;
        float now = Time.unscaledTime;
        InteractionLease lease = InteractionLeases[service];
        if (LiveInteraction(lease.Player, service, lease.Session, now)) return lease.Player;
        lease.Player = 0; lease.Session = 0;
        int owner = 0; uint sessionId = 0; float oldestAge = float.NegativeInfinity;
        if (PrivateLane.Active && PrivateLane.Service == service)
        {
            owner = LocalPeer; sessionId = PrivateLane.Session;
            oldestAge = Mathf.Max(0f, now - PrivateLane.Started);
        }
        foreach (var pair in VisitorSessions)
        {
            TownServiceSessionInfo session = pair.Value;
            if (pair.Key <= 0 || !session.Active || session.Service != service
                || now - session.LastSeenTime > NetProtocol.StaleTimeoutSeconds) continue;
            float age = session.SessionAge + Mathf.Max(0f, now - session.ReceivedTime);
            if (owner == 0 || age > oldestAge + .05f
                || Mathf.Abs(age - oldestAge) <= .05f && pair.Key < owner)
            { owner = pair.Key; sessionId = session.Session; oldestAge = age; }
        }
        if (owner == 0) { lease.PendingSince = float.NegativeInfinity; return 0; }
        if (float.IsNegativeInfinity(lease.PendingSince))
        { lease.PendingSince = now; return 0; }
        if (now - lease.PendingSince < InteractionClaimSettleSeconds) return 0;
        lease.Player = owner; lease.Session = sessionId;
        lease.PendingSince = float.NegativeInfinity;
        return owner;
    }

    /// <summary>Reservation of one NPC after a physical offer is placed. The three leases are
    /// independent. Mere proximity and native window opening never claim a transaction.
    /// A lost peer, closed native session, reclaimed offer or scene reset releases it.</summary>
    internal static int TransactionOwner(byte service)
    {
        if (service < 1 || service > 3) return 0;
        // The priestess never occupies an exclusive NPC transaction. Her short
        // reliable native-commit reservation still gates LocalTransactionSettled,
        // but must not lock another visitor's purse or select a different shared pose.
        if (service == 2) return 0;
        // The host's ReliableOrdered grant outranks the lossy visual claim manifest.
        // Under two simultaneous drops the older browsing session may not be the first
        // granted physical offer, so TLV92 cannot decide who can use original callbacks.
        int granted = TownServiceGrantSync.GrantedOwner(service);
        if (granted != 0) return granted;
        float now = Time.unscaledTime;
        InteractionLease lease = TransactionLeases[service];
        if (LiveInteraction(lease.Player, service, lease.Session, now, requireTransaction: true))
            return lease.Player;
        lease.Player = 0; lease.Session = 0;
        int owner = 0; uint sessionId = 0; float oldestAge = float.NegativeInfinity;
        if (PrivateLane.Active && PrivateLane.Service == service && PrivateLane.TransactionActive)
        { owner = LocalPeer; sessionId = PrivateLane.Session; oldestAge = Mathf.Max(0f, now - PrivateLane.Started); }
        foreach (var pair in VisitorSessions)
        {
            TownServiceSessionInfo session = pair.Value;
            if (pair.Key <= 0 || !session.Active || !session.TransactionActive
                || session.Service != service || now - session.LastSeenTime > NetProtocol.StaleTimeoutSeconds) continue;
            float age = session.SessionAge + Mathf.Max(0f, now - session.ReceivedTime);
            if (owner == 0 || age > oldestAge + .05f
                || Mathf.Abs(age - oldestAge) <= .05f && pair.Key < owner)
            { owner = pair.Key; sessionId = session.Session; oldestAge = age; }
        }
        if (owner == 0) { lease.PendingSince = float.NegativeInfinity; return 0; }
        if (float.IsNegativeInfinity(lease.PendingSince)) { lease.PendingSince = now; return 0; }
        if (now - lease.PendingSince < InteractionClaimSettleSeconds) return 0;
        lease.Player = owner; lease.Session = sessionId;
        lease.PendingSince = float.NegativeInfinity;
        return owner;
    }

    private static bool AnyTransactionClaim(byte service)
    {
        if (PrivateLane.Active && PrivateLane.Service == service && PrivateLane.TransactionActive) return true;
        float now = Time.unscaledTime;
        foreach (TownServiceSessionInfo remote in VisitorSessions.Values)
            if (remote.Active && remote.Service == service && remote.TransactionActive
                && now - remote.LastSeenTime <= NetProtocol.StaleTimeoutSeconds) return true;
        return false;
    }

    private static bool LiveInteraction(int player, byte service, uint session, float now,
        bool requireTransaction = false)
    {
        if (player <= 0 || session == 0) return false;
        if (player == LocalPeer) return PrivateLane.Active && PrivateLane.Service == service
            && PrivateLane.Session == session && (!requireTransaction || PrivateLane.TransactionActive);
        return VisitorSessions.TryGetValue(player, out TownServiceSessionInfo? remote)
            && remote.Active && remote.Service == service && remote.Session == session
            && (!requireTransaction || remote.TransactionActive)
            && now - remote.LastSeenTime <= NetProtocol.StaleTimeoutSeconds;
    }

    internal static bool LocalOwnsInteraction(byte service, uint session)
    {
        if (session == 0 || !PrivateLane.Active || PrivateLane.Service != service
            || PrivateLane.Session != session) return false;
        if (service == 2) return true;
        // Every visitor at the same NPC may browse until a physical offer is placed.
        // The elected shared-animation author is not an exclusive interaction lock.
        int granted = TownServiceGrantSync.GrantedOwner(service);
        if (granted != 0) return granted == LocalPeer;
        foreach (TownServiceSessionInfo remote in VisitorSessions.Values)
            if (remote.Active && remote.Service == service && remote.TransactionActive
                && Time.unscaledTime - remote.LastSeenTime <= NetProtocol.StaleTimeoutSeconds
                && !PrivateLane.TransactionActive) return false;
        return true;
    }

    /// <summary>Pre-drop affordance for one NPC. A local visitor may browse all three
    /// residents at once; only a different visitor's already parked transaction at this
    /// exact resident suppresses a second offer. Simultaneous first drops are resolved by
    /// the host grant before either original native callback may execute.</summary>
    internal static bool CanLocalBeginTransaction(byte service)
    {
        if (service < 1 || service > 3) return false;
        if (service == 2) return true;
        int granted = TownServiceGrantSync.GrantedOwner(service);
        if (granted != 0) return granted == LocalPeer;
        float now = Time.unscaledTime;
        foreach (var pair in VisitorSessions)
            if (pair.Key > 0 && pair.Key != LocalPeer && pair.Value.Active
                && pair.Value.Service == service && pair.Value.TransactionActive
                && now - pair.Value.LastSeenTime <= NetProtocol.StaleTimeoutSeconds)
                return false;
        return true;
    }

    /// <summary>The maintainer explicitly permits one shared pre-drop enchantress cue
    /// instead of overlapping each visitor's identical hologram (NPC test 2026-10-03).
    /// This is only a renderer election: all eligible visitors retain the same physical
    /// drop collider, hover feedback and reliable first-offer claim.</summary>
    internal static bool CanShowLocalCue(byte service) => service != 3
        || TownServiceSharedCue.GuideOwner == LocalPeer;

    internal static void SetLocalTransactionActive(byte service, bool active)
    {
        if (!PrivateLane.Active || PrivateLane.Service != service || PrivateLane.Session == 0) return;
        bool changed = PrivateLane.TransactionActive != active;
        if (changed)
        {
            PrivateLane.TransactionActive = active;
            PrivateLane.NextManifest = 0f;
        }
        // Reassert idempotently even when the visual state did not change: a transport
        // reset can retire the grant while the same physical card remains parked.
        TownServiceGrantSync.SetOffer(service, PrivateLane.Session, active);
        if (changed && !active) TransactionOwner(service); // retire a released local lease immediately
    }

    /// <summary>Local pre-native gate for an already parked card or purse. It stays false
    /// during the bounded claim election and whenever another visitor owns this NPC.
    /// The original native callback must wait for a matching ReliableOrdered host grant.
    /// The lossy manifest claim remains presentation-only.</summary>
    internal static bool LocalTransactionSettled(byte service) => PrivateLane.Active
        && PrivateLane.Service == service && PrivateLane.TransactionActive
        && TownServiceGrantSync.MayCommit(service, PrivateLane.Session);

    internal static bool LocalTransactionUnavailable(byte service) => PrivateLane.Active
        && PrivateLane.Service == service && PrivateLane.TransactionActive
        && TownServiceGrantSync.Unavailable(service, PrivateLane.Session);

    internal static bool LocalTransactionDenied(byte service) => PrivateLane.Active
        && PrivateLane.Service == service && PrivateLane.TransactionActive
        && TownServiceGrantSync.Denied(service, PrivateLane.Session);

    internal static bool IsInteractionOwner(int player, byte service, uint session)
    {
        if (player <= 0 || session == 0 || InteractionOwner(service) != player) return false;
        if (player == LocalPeer) return PrivateLane.Active && PrivateLane.Service == service
            && PrivateLane.Session == session;
        return VisitorSessions.TryGetValue(player, out TownServiceSessionInfo? remote)
            && remote.Active && remote.Service == service && remote.Session == session
            && Time.unscaledTime - remote.LastSeenTime <= NetProtocol.StaleTimeoutSeconds;
    }

    internal static bool TryInteractionOwner(byte service, out int player, out uint session, out float age)
    {
        player = InteractionOwner(service); session = 0; age = 0f;
        if (player == 0) return false;
        if (player == LocalPeer)
        {
            session = PrivateLane.Session;
            age = Mathf.Max(0f, Time.unscaledTime - PrivateLane.Started);
            return PrivateLane.Active && PrivateLane.Service == service;
        }
        if (!VisitorSessions.TryGetValue(player, out TownServiceSessionInfo? remote)) return false;
        session = remote.Session;
        age = remote.SessionAge + Mathf.Max(0f, Time.unscaledTime - remote.ReceivedTime);
        return remote.Active && remote.Service == service;
    }

    /// <summary>Publish the native donation affordance sampled by the elected local temple
    /// visitor. Unknown deliberately reads as available: an observer must never cover the bowl
    /// merely because the owner's first eligibility sample has not arrived yet.</summary>
    internal static void SetLocalTempleDonationAvailable(bool available)
    {
        if (!PrivateLane.Active || PrivateLane.Service != 2) return;
        if (PrivateLane.TempleDonationKnown && PrivateLane.TempleDonationAvailable == available) return;
        PrivateLane.TempleDonationKnown = true;
        PrivateLane.TempleDonationAvailable = available;
        PrivateLane.NextManifest = 0f;
    }

    /// <summary>Only the original native donation success callback may advance this revision.
    /// A change of selected character or affordability is merely availability, not a blessing.</summary>
    internal static void MarkLocalTempleDonationCommitted()
    {
        if (!PrivateLane.Active || PrivateLane.Service != 2) return;
        unchecked { PrivateLane.TempleDonationRevision++; }
        if (PrivateLane.TempleDonationRevision == 0) PrivateLane.TempleDonationRevision = 1;
        PrivateLane.TempleDonationChangedTime = Time.unscaledTime;
        PrivateLane.TempleDonationKnown = true;
        PrivateLane.TempleDonationAvailable = false;
        PrivateLane.NextManifest = 0f;
        ObserveTempleDonationCommit(LocalPeer, PrivateLane.Session,
            PrivateLane.TempleDonationRevision, 0f);
    }

    private sealed class DonationCommit
    {
        internal uint Revision;
        internal float ChangedTime;
    }
    // Donation events are independent of browsing/window lifetime. The Build609
    // co-player's original callback ran, while bulk native modules remained behind
    // missing assets and their queues. Closing that visit must not discard the only
    // proof of the blessing. The fast numeric lane republishes this bounded event;
    // it never reconstructs a donation from availability or a local selected hero.
    private static readonly Dictionary<(int Peer, uint Session), DonationCommit> DonationCommits = new();
    private static readonly Queue<(int Peer, uint Session)> DonationCommitOrder = new();
    internal static void ObserveTempleDonationCommit(int peer, uint session, uint revision, float age)
    {
        if (peer <= 0 || session == 0 || revision == 0 || float.IsNaN(age)
            || float.IsInfinity(age) || age < 0f || age > 30f) return;
        var key = (peer, session);
        if (DonationCommits.TryGetValue(key, out DonationCommit? old))
        {
            if (old.Revision == revision) return; // repeated fast/presence clocks never restart it
            if (unchecked((int)(revision - old.Revision)) <= 0) return;
        }
        else
        {
            old = new DonationCommit(); DonationCommits.Add(key, old); DonationCommitOrder.Enqueue(key);
            if (DonationCommitOrder.Count > 64) DonationCommits.Remove(DonationCommitOrder.Dequeue());
        }
        old.Revision = revision; old.ChangedTime = Time.unscaledTime - age;
    }
    internal static bool TryLocalTempleDonationCommit(out uint session, out uint revision, out float age)
    {
        session = revision = 0; age = 0f;
        float latest = float.NegativeInfinity;
        foreach (var pair in DonationCommits)
            if (pair.Key.Peer == LocalPeer && pair.Value.ChangedTime > latest)
            { session = pair.Key.Session; revision = pair.Value.Revision; latest = pair.Value.ChangedTime; }
        if (session == 0) return false;
        age = Mathf.Max(0f, Time.unscaledTime - latest);
        return age <= 30f;
    }

    internal static void CollectTempleDonationStates(List<TownTempleDonationState> destination)
    {
        destination.Clear();
        float now = Time.unscaledTime;
        if (PrivateLane.Active && PrivateLane.Service == 2)
            destination.Add(new TownTempleDonationState(LocalPeer, PrivateLane.Session,
                PrivateLane.TempleDonationKnown, PrivateLane.TempleDonationAvailable,
                PrivateLane.TempleDonationRevision,
                PrivateLane.TempleDonationRevision == 0 ? 0f : Mathf.Max(0f, now - PrivateLane.TempleDonationChangedTime),
                hasCommitAge: true));
        foreach (var pair in VisitorSessions)
        {
            TownServiceSessionInfo visitor = pair.Value;
            if (pair.Key <= 0 || !visitor.Active || visitor.Service != 2
                || now - visitor.LastSeenTime > NetProtocol.StaleTimeoutSeconds) continue;
            destination.Add(new TownTempleDonationState(pair.Key, visitor.Session,
                visitor.TempleDonationKnown, visitor.TempleDonationAvailable,
                visitor.TempleDonationRevision,
                visitor.TempleDonationRevision == 0 ? 0f : Mathf.Max(0f, now - visitor.TempleDonationChangedTime),
                hasCommitAge: visitor.HasTempleDonationCommitAge));
        }
        foreach (var pair in DonationCommits)
        {
            float age = Mathf.Max(0f, now - pair.Value.ChangedTime);
            if (age > 30f) continue;
            bool alreadyIncluded = false;
            for (int i = 0; i < destination.Count; i++)
                if (destination[i].Peer == pair.Key.Peer && destination[i].Session == pair.Key.Session)
                {
                    alreadyIncluded = true;
                    if (unchecked((int)(pair.Value.Revision - destination[i].Revision)) > 0
                        || pair.Value.Revision == destination[i].Revision && !destination[i].HasCommitAge)
                        destination[i] = new TownTempleDonationState(pair.Key.Peer, pair.Key.Session,
                            true, false, pair.Value.Revision, age, hasCommitAge: true);
                    break;
                }
            if (!alreadyIncluded) destination.Add(new TownTempleDonationState(pair.Key.Peer, pair.Key.Session,
                true, false, pair.Value.Revision, age, hasCommitAge: true));
        }
    }

    internal static bool TempleDonationAvailable
    {
        get
        {
            int owner = InteractionOwner(2);
            if (owner == LocalPeer) return !PrivateLane.TempleDonationKnown || PrivateLane.TempleDonationAvailable;
            return owner == 0 || !VisitorSessions.TryGetValue(owner, out TownServiceSessionInfo? remote)
                || !remote.TempleDonationKnown || remote.TempleDonationAvailable;
        }
    }

    /// <summary>Presentation availability across all current temple visitors. The bowl is
    /// covered only if every visitor has a fresh, known native denial. An unopened or delayed
    /// native sample cannot deny another player's valid donation. This does not authorize a
    /// transaction: each visitor's original temple controller still validates their purse.</summary>
    internal static bool TryTemplePresentationState(out bool hasVisitor, out bool anyCanDonate)
    {
        float now = Time.unscaledTime;
        hasVisitor = false;
        anyCanDonate = false;
        if (PrivateLane.Active && PrivateLane.Service == 2)
        {
            hasVisitor = true;
            anyCanDonate = !PrivateLane.TempleDonationKnown || PrivateLane.TempleDonationAvailable;
        }
        foreach (TownServiceSessionInfo visitor in VisitorSessions.Values)
        {
            if (!visitor.Active || visitor.Service != 2
                || now - visitor.LastSeenTime > NetProtocol.StaleTimeoutSeconds) continue;
            hasVisitor = true;
            if (!visitor.TempleDonationKnown || visitor.TempleDonationAvailable)
                anyCanDonate = true;
        }
        return hasVisitor;
    }

    internal static bool TryTempleDonationState(out int owner, out uint session, out bool known,
        out bool available, out uint revision, out float transitionAge)
    {
        owner = InteractionOwner(2); session = 0; known = false; available = true;
        revision = 0; transitionAge = 0f;
        if (owner == 0) return false;
        if (owner == LocalPeer)
        {
            session = PrivateLane.Session; known = PrivateLane.TempleDonationKnown;
            available = !known || PrivateLane.TempleDonationAvailable;
            revision = PrivateLane.TempleDonationRevision;
            transitionAge = revision == 0 ? 0f : Mathf.Max(0f,
                Time.unscaledTime - PrivateLane.TempleDonationChangedTime);
            return true;
        }
        if (!VisitorSessions.TryGetValue(owner, out TownServiceSessionInfo? remote)) return false;
        session = remote.Session; known = remote.TempleDonationKnown;
        available = !known || remote.TempleDonationAvailable;
        revision = remote.TempleDonationRevision;
        transitionAge = revision == 0 ? 0f : Mathf.Max(0f,
            Time.unscaledTime - remote.TempleDonationChangedTime);
        return true;
    }
    internal static int PublicAuthor
    {
        get
        {
            int author = PublicLane.Active ? LocalPeer : int.MaxValue;
            uint claim = PublicLane.Active ? _publicClaim : 0;
            foreach (var pair in Sessions)
            {
                var session = pair.Value;
                if (pair.Key >= 0 || IsStockPeerKey(pair.Key) || !session.Active || Time.unscaledTime - session.LastSeenTime > 10f) continue;
                int peer = -pair.Key;
                if (session.PublicClaim > claim || session.PublicClaim == claim && peer < author)
                { author = peer; claim = session.PublicClaim; }
            }
            return author;
        }
    }
    internal static bool IsPublicAuthor => PublicAuthor == LocalPeer;
    internal static void ClaimPublicCatalog()
    {
        if (IsPublicAuthor) return;
        if (_observedPublicClaim == uint.MaxValue) return;
        _publicClaim = _observedPublicClaim + 1; _observedPublicClaim = _publicClaim;
        PublicLane.NextManifest = 0;
        foreach (LocalModule module in PublicLane.Modules.Values)
        { module.Last = null; module.Baseline = null; module.NextBaseline = module.NextRefresh = 0; }
    }
    internal static TownRackState? PublicRack
    {
        get
        {
            int peer = -PublicAuthor;
            // Authority handoff adopts what observers are displaying, including elapsed
            // analytic motion since the last packet. A stale owner sample must not rewind
            // a cassette which already completed while that owner disconnected.
            if (RemoteRacks.TryGetValue(peer, out var clocks))
                foreach (RackPlayback clock in clocks.Values)
                    if (clock.State != null)
                    {
                        // The displayed rack may wait for a native face/baseline, but that
                        // cosmetic wait must never become the local input proxy's clock.
                        // Build 601's mismatched first-press rack repeatedly returned
                        // Elapsed=0 here; Follow then kept Accessible false forever on the
                        // other player's real category buttons. A claiming visitor can
                        // render the same native stock locally, so adopt the latest owner
                        // mechanism age while its observer dependencies are pending.
                        if (clock.Waiting && clock.Latest != null)
                        {
                            TownRackState ready = clock.Latest.Copy();
                            ready.Elapsed = Mathf.Clamp(ready.Elapsed + Mathf.Max(0f,
                                Time.unscaledTime - clock.ReceivedTime), 0f, TownRackState.TurnDuration);
                            ready.Page = TownRackState.Progress(ready.Elapsed) < .5f ? ready.From : ready.To;
                            return ready;
                        }
                        if (!ReferenceEquals(clock.HandoffSource, clock.State))
                        { clock.HandoffSource = clock.State; clock.Handoff = clock.State.Copy(); }
                        TownRackState displayed = clock.Handoff!;
                        displayed.Page = clock.DisplayPage; displayed.From = clock.FromPage;
                        displayed.Elapsed = clock.Turning ? clock.Elapsed : TownRackState.TurnDuration;
                        return displayed;
                    }
            if (Pending.TryGetValue(peer, out var modules))
                foreach (TownServiceFrame frame in modules.Values) if (frame.Rack != null) return frame.Rack;
            return null;
        }
    }
    private sealed class LocalLane
    {
        internal readonly Dictionary<ushort, LocalModule> Modules = new();
        internal readonly Dictionary<Transform, ParentLink> Parents = new();
        internal readonly Dictionary<ushort, TownRackState> Racks = new();
        internal readonly Dictionary<ushort, TownRackStamp> Members = new();
        internal readonly Dictionary<ushort, CanvasGroup> Gates = new();
        internal Transform? SharedFrame, Station;
        internal byte Service; internal uint Session; internal ulong Sequence;
        internal bool Active; internal float NextManifest, ClosedUntil, Started; internal ushort Heartbeat;
        internal bool TempleDonationKnown, TempleDonationAvailable;
        internal uint TempleDonationRevision;
        internal float TempleDonationChangedTime;
        internal bool TransactionActive;
    }
    private static readonly LocalLane PrivateLane = new(), PublicLane = new(), StockLane = new();
    private static LocalLane _local = PrivateLane;
    internal static IDisposable UsePublicLane() => new LaneScope(PublicLane);
    internal static IDisposable UseStockLane() => new LaneScope(StockLane);
    private sealed class LaneScope : IDisposable
    {
        private readonly LocalLane _previous;
        internal LaneScope(LocalLane lane) { _previous = _local; _local = lane; }
        public void Dispose() => _local = _previous;
    }
    private sealed class LocalModule
    {
        internal ushort Id, Template;
        internal string Address = string.Empty;
        internal TownServiceBinding Binding = null!;
        internal TownServiceFrame? Last;
        internal TownServiceFrame? Baseline;
        internal float NextBaseline;
        internal float NextRefresh;
        internal float RetryAfter;
        internal bool HighPriority, WasPriority;
        internal ulong LastSent;
        internal readonly TownServiceFrame Probe = new();
        internal Func<Transform, bool>? Exclude;
    }
    private sealed class RemoteModule : IDisposable
    {
        internal GameObject Host = null!;
        internal Canvas? AddedCanvas;
        internal TownServiceBinding Binding = null!;
        internal uint Session;
        internal ushort Template;
        internal string Address = string.Empty;
        internal ulong Sequence;
        internal TownServiceFrame? LastFrame;
        internal Renderer[]? RackBodyRenderers;
        internal bool StockMasked, PublicMasked;
        internal TownServiceMotion Motion = null!;
        internal bool Alive => Host != null && Binding.Root != null;
        public void Dispose()
        { Binding.Dispose(); if (Host != null) { Host.SetActive(false); Object.Destroy(Host); } }
    }

    private static void RetireDestroyedRemoteModules(int peer, Dictionary<ushort, RemoteModule> modules)
    {
        RetiredRemoteChildren.Clear();
        foreach (var pair in modules)
            if (!pair.Value.Alive) RetiredRemoteChildren.Add(pair.Key);
        int count = RetiredRemoteChildren.Count;
        foreach (ushort id in RetiredRemoteChildren)
        {
            RemoteModule dead = modules[id];
            dead.Dispose(); modules.Remove(id);
        }
        if (count != 0)
            Report("remote peer " + peer,
                new InvalidDataException(count + " town-service observer host(s) were destroyed; rebuilding from retained owner frames."));
        RetiredRemoteChildren.Clear();
    }

    private static void RetireRemoteDescendants(Dictionary<ushort, RemoteModule> modules, ushort parentId, RemoteModule parent)
    {
        if (!parent.Alive) return;
        Transform host = parent.Host.transform;
        RetiredRemoteChildren.Clear();
        foreach (var pair in modules)
            if (pair.Key != parentId && pair.Value.Alive && pair.Value.Host.transform.IsChildOf(host))
                RetiredRemoteChildren.Add(pair.Key);
        foreach (ushort id in RetiredRemoteChildren)
        { modules[id].Dispose(); modules.Remove(id); }
        RetiredRemoteChildren.Clear();
    }

    internal static void RegisterTemplate(byte service, ushort template, Transform original,
        Func<Transform, bool>? exclude = null, string address = "")
    {
        string key = TemplateKey(service, template, address);
        if (Templates.TryGetValue(key, out GameObject? existing) && existing != null) return;
        if (original == null) throw new ArgumentException("Native town-service template is absent.");
        if (_templateHost == null)
        {
            _templateHost = new GameObject("GVR inert town-service template bank");
            _templateHost.SetActive(false); Object.DontDestroyOnLoad(_templateHost);
        }
        // Instantiate below an inactive parent BEFORE stripping components; no Awake/OnEnable.
        GameObject clone = Object.Instantiate(original.gameObject, _templateHost.transform, false);
        Prune(original, clone.transform, exclude);
        TownServiceNeutralize.Apply(clone);
        PrepareInertGeometry?.Invoke(address, clone);
        foreach (UnityEngine.UI.Graphic graphic in clone.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
        clone.SetActive(false);
        using var check = new TownServiceBinding(clone.transform);
        Templates[key] = clone;
    }

    internal static void ForgetTemplates(byte service, string addressPrefix)
    {
        // Called only after the corresponding resident has no local or remote visitors.
        // Its original Addressables/material owners may now be released; a later resident
        // must freeze fresh rendering resources, not reuse a dead decoration template.
        string prefix = service + ":" + addressPrefix;
        var keys = new List<string>();
        foreach (var pair in Templates) if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(pair.Key);
        foreach (string key in keys)
        { if (Templates[key] != null) Object.Destroy(Templates[key]); Templates.Remove(key); }
    }

    private static void Prune(Transform source, Transform copy, Func<Transform, bool>? exclude)
    {
        for (int i = source.childCount - 1; i >= 0; i--)
        {
            Transform child = source.GetChild(i), duplicate = copy.GetChild(i);
            if (TownServiceBinding.GeneratedTextMesh(child) || (exclude != null && exclude(child))) Object.DestroyImmediate(duplicate.gameObject);
            else Prune(child, duplicate, exclude);
        }
    }

    internal static void BeginSession(byte service, uint session, Transform sharedFrame, Transform stationAnchor, float ownerAge = 0f)
    {
        TemplateKey(service, 1);
        if (session == 0 || sharedFrame == null || stationAnchor == null) throw new ArgumentException("Missing town-service session frame.");
        if (_session != session || _service != service)
        { ClearLocalModules(); ClearLaneVoiceOutgoing(); _nextManifest = 0; _sessionStarted = Time.unscaledTime - Mathf.Max(0f, ownerAge);
          _local.TempleDonationKnown = _local.TempleDonationAvailable = false;
          _local.TempleDonationRevision = 0; _local.TempleDonationChangedTime = 0f;
          _local.TransactionActive = false; }
        _service = service; _session = session; _sharedFrame = sharedFrame; _station = stationAnchor; _active = true;
    }

    internal static void RegisterModule(ushort module, ushort template, Transform liveRoot,
        Func<Transform, bool>? exclude = null, string address = "")
    {
        if (!_active || module >= TownServiceFrame.VoiceModule || liveRoot == null
            || Local.Count >= TownServiceFrame.MaxModules && !Local.ContainsKey(module))
            throw new ArgumentException("Invalid live town-service module.");
        if (!Templates.ContainsKey(TemplateKey(_service, template, address)))
            throw new InvalidOperationException("Register the original template before its town-service module.");
        if (Local.TryGetValue(module, out LocalModule? current))
        {
            if (current.Template == template && current.Address == address && ReferenceEquals(current.Binding.Root, liveRoot)) return;
            current.Binding.Dispose();
        }
        Local[module] = new LocalModule { Id = module, Template = template, Address = address, Binding = new TownServiceBinding(liveRoot, exclude), Exclude = exclude };
        _nextManifest = 0;
    }

    internal static void UnregisterModule(ushort module)
    {
        if (!Local.TryGetValue(module, out LocalModule? current)) return;
        current.Binding.Dispose(); Local.Remove(module); LocalRacks.Remove(module); LocalRackMembers.Remove(module); LocalRackGates.Remove(module); _nextManifest = 0;
    }

    internal static void SetPriority(ushort module, bool highPriority)
    { if (Local.TryGetValue(module, out LocalModule? current)) current.HighPriority = highPriority; }

    private static void SnapshotSent(TownServiceFrame frame)
    {
        using var lane = new LaneScope(frame.VisitorStock ? StockLane : frame.PublicCatalog ? PublicLane : PrivateLane);
        if (frame.Session != _session || frame.Service != _service || !Local.TryGetValue(frame.Module, out LocalModule? module)) return;
        module.LastSent = Math.Max(module.LastSent, frame.Sequence);
        float now = Time.unscaledTime;
        if (frame.BaseSequence == 0 && module.Baseline?.Sequence == frame.Sequence)
            module.NextBaseline = now + 5f + module.Id % 13 * .07f;
        // A single small ordinary module maintains session liveness; unchanged stock
        // does not need 2,000 separate subsecond heartbeat packets behind it.
        module.NextRefresh = now + (NeedsHeartbeat(module) ? .75f : 5f + module.Id % 7 * .03f);
    }

    // Offering membership expires independently of other traffic. Its exact owner
    // sample must stay fresh even when an earlier allocated module owns the generic
    // session heartbeat. This adds one small urgent packet per .75 s, not a stream
    // of unchanged catalog cards or per-frame intent packets.
    private static bool NeedsHeartbeat(LocalModule module) => module.Id == _heartbeatModule
        || ReferenceEquals(_local, PrivateLane) && _service == 1 && module.Address == MerchantOfferingAddress;

    internal static void EndSession()
    { if (ReferenceEquals(_local, PrivateLane) && _local.TransactionActive)
          TownServiceGrantSync.SetOffer(_local.Service, _local.Session, false);
      _active = false; _closedUntil = Time.unscaledTime + 5; _nextManifest = 0;
      _local.TempleDonationKnown = _local.TempleDonationAvailable = false;
      _local.TempleDonationRevision = 0; _local.TempleDonationChangedTime = 0f;
      _local.TransactionActive = false;
      ClearLaneVoiceOutgoing(); ClearLocalModules(); }

    /// <summary>Call in the owner's final presentation pass. Immutable packets go to the existing transport.</summary>
    internal static void Capture(Action<byte[], int> send) => CaptureCore((bytes, length, _) => send(bytes, length), false);
    internal static void Capture(Action<byte[], int, object?> send) => CaptureCore(send, true);
    private static void CaptureCore(Action<byte[], int, object?> send, bool fast)
    {
        // A caller without the independent motion subscriber still receives complete
        // native pictures. Only the production transport can bypass numeric art deltas.
        FastMotionCaptureEnabled = fast;
        try
        {
            using (new LaneScope(PrivateLane)) { CaptureLane(send); CaptureVoice(send); }
            using (new LaneScope(PublicLane)) CaptureLane(send);
            using (new LaneScope(StockLane)) { CaptureLane(send); CaptureStockVoice(send); }
            if (fast) CaptureMotion(send);
        }
        finally { FastMotionCaptureEnabled = false; }
    }
    private static void CaptureLane(Action<byte[], int, object?> send)
    {
        float now = Time.unscaledTime;
        if (_session == 0 || _sharedFrame == null || (!_active && now > _closedUntil)) return;
        if (_active)
        {
            _heartbeatModule = ushort.MaxValue;
            foreach (ushort id in Local.Keys) if (id < _heartbeatModule) _heartbeatModule = id;
            SourceParents.Clear();
            foreach (LocalModule source in Local.Values)
                for (int i = 0; i < source.Binding.Nodes.Length; i++)
                    if (source.Binding.Nodes[i] != null) SourceParents[source.Binding.Nodes[i]] = new ParentLink(source.Id, source.Binding.Bindings[i]);
            foreach (LocalModule module in Local.Values)
            {
                if (now < module.RetryAfter) continue;
                try
                {
                    Transform source = module.Binding.Root;
                    if (source == null) continue;
                    TownServiceNode[] nodes;
                    try { nodes = module.Binding.Read(Assets); }
                    catch (InvalidDataException e) when (e.Message == "Native town-service topology changed.")
                    {
                        module.Binding.Dispose(); module.Binding = new TownServiceBinding(source, module.Exclude);
                        nodes = module.Binding.Read(Assets);
                    }
                    // Reuse only the unpublished probe. Emitted headers/node arrays are
                    // retained separately, so a later read cannot mutate queued artwork.
                    TownServiceFrame frame = module.Probe;
                    frame.VisitorStock = ReferenceEquals(_local, StockLane); frame.PublicCatalog = ReferenceEquals(_local, PublicLane); frame.PublicClaim = frame.PublicCatalog ? _publicClaim : 0;
                    frame.Service = _service; frame.Session = _session; frame.Module = module.Id;
                    frame.Template = module.Template; frame.TemplateAddress = module.Address; frame.Structure = module.Binding.Structure;
                    frame.Visible = source.gameObject.activeInHierarchy; frame.SampleTime = now;
                    frame.Pose = ReadPose(source, _sharedFrame, frame.Pose); frame.Nodes = nodes;
                    frame.RackMember = LocalRackMembers.TryGetValue(module.Id, out TownRackStamp? rackMember) ? rackMember : null;
                    frame.Rack = LocalRacks.TryGetValue(module.Id, out TownRackState? rackState) ? rackState : null;
                    frame.WorkspaceCloth = null;
                    frame.SessionAge = Mathf.Max(0f, now - _sessionStarted);
                    frame.ParentModule = TownServiceFrame.ManifestModule; frame.ParentBinding = 0;
                    ResetCanvasFrame(frame);
                    ReadParent(module, frame); ReadCanvasFrame(source, frame);
                    if (frame.RackMember != null)
                    {
                        float alpha = ReadRackAlpha(module);
                        if (frame.RackMember.Alpha != alpha) { frame.RackMember = frame.RackMember.Copy(); frame.RackMember.Alpha = alpha; }
                    }
                    if (FastMotionCaptureEnabled && module.Last != null && module.Baseline != null
                        && now < module.NextBaseline && module.WasPriority == module.HighPriority
                        && (!NeedsHeartbeat(module) || now < module.NextRefresh)
                        && TownServiceFastNumbers.SameArtwork(module.Last, frame))
                    {
                        if (!SamePresentation(module.Last, frame))
                        {
                            frame.Sequence = module.Last.Sequence;
                            module.Last = TownServiceDelta.Retain(frame);
                        }
                        continue;
                    }
                    bool sameSurface = SamePresentation(module.Last, frame);
                    if (sameSurface && module.WasPriority == module.HighPriority
                        && (now < module.NextRefresh || module.Last != null && module.LastSent < module.Last.Sequence)) continue;
                    frame.Sequence = NextSequence();
                    TownServiceFrame emitted;
                    if (module.Baseline == null || now >= module.NextBaseline || !TownServiceDelta.Compatible(module.Baseline, frame))
                    { emitted = TownServiceDelta.Retain(frame); module.Baseline = emitted; module.NextBaseline = float.PositiveInfinity; }
                    else emitted = TownServiceDelta.Create(module.Baseline, frame);
                    emitted.HighPriority = module.HighPriority || module.WasPriority || NeedsHeartbeat(module);
                    module.WasPriority = module.HighPriority;
                    byte[] packet = TownServiceCodec.Write(emitted);
                    send(packet, packet.Length, emitted); module.Last = TownServiceDelta.Retain(frame); module.NextRefresh = now + .75f + module.Id % 7 * .03f;
                }
                catch (Exception e) { module.RetryAfter = now + 1; Report("capture module " + module.Id, e); }
            }
        }
        if (now >= _nextManifest)
        {
            try
            {
                var ids = new List<ushort>(Local.Keys); ids.Sort();
                var manifest = new TownServiceFrame { VisitorStock = ReferenceEquals(_local, StockLane), PublicCatalog = ReferenceEquals(_local, PublicLane), PublicClaim = ReferenceEquals(_local, PublicLane) ? _publicClaim : 0, Service = _service, Session = _session,
                    Module = TownServiceFrame.ManifestModule, Sequence = NextSequence(), SampleTime = now,
                    SessionAge = now - _sessionStarted,
                    Visible = _active, Modules = ids.ToArray(), Pose = _station != null ? ReadPose(_station, _sharedFrame) : IdentityPose() };
                if (ReferenceEquals(_local, PrivateLane) && _service == 2 && _active && _local.TempleDonationKnown)
                { manifest.TempleDonationKnown = true; manifest.TempleDonationAvailable = _local.TempleDonationAvailable;
                  manifest.TempleDonationRevision = _local.TempleDonationRevision;
                  manifest.HasTempleDonationCommitAge = true;
                  manifest.TempleDonationCommitAge = _local.TempleDonationRevision == 0 ? 0f
                    : Mathf.Clamp(now - _local.TempleDonationChangedTime, 0f, 30f); }
                if (ReferenceEquals(_local, PrivateLane) && _active)
                    manifest.TransactionActive = _local.TransactionActive;
                byte[] packet = TownServiceCodec.Write(manifest); send(packet, packet.Length, manifest);
                // Private manifests are also the deterministic interaction lease heartbeat.
                // Keep it below the stale timeout even when a service currently has no modules.
                _nextManifest = now + (_active && !ReferenceEquals(_local, PublicLane) ? .75f : _active ? 5f : .5f);
            }
            catch (Exception e) { Report("capture manifest", e); }
        }
    }

    /// <summary>Receives a complete GVR1 module packet after ExtrasFragments assembly.</summary>
    internal static bool Receive(int peer, byte[] packet, int length)
    {
        if (peer <= 0 || !TownServiceCodec.TryRead(packet, length, out TownServiceFrame? frame)) return false;
        if (frame!.Module == TownServiceFrame.VoiceModule)
        { if (frame.VisitorStock) ReceiveStockVoice(peer, frame); else ReceiveVoice(peer, frame); return true; }
        if (frame!.VisitorStock)
        { if (!TryStockPeerKey(peer, out peer)) return false; }
        else if (frame!.PublicCatalog)
        { if (IsStockPeerKey(-peer)) { Report("stock peer namespace", new InvalidDataException("Public peer collides with a reserved stock presentation key.")); return false; }
          peer = -peer; _observedPublicClaim = Math.Max(_observedPublicClaim, frame.PublicClaim); }
        if (!Sessions.ContainsKey(peer) && Sessions.Count >= 24) return true;
        if (frame!.Module == TownServiceFrame.ManifestModule)
        {
            if (Sessions.TryGetValue(peer, out TownServiceSessionInfo? previous) && frame.Sequence <= previous.Sequence) return true;
            if (previous != null && (previous.Session != frame.Session || previous.Service != frame.Service))
            { ForgetPublicPicture(peer); ClearRemoteModules(peer); }
            bool donationAdvanced = previous != null && previous.Session == frame.Session && previous.Service == frame.Service
                && previous.TempleDonationKnown && frame.TempleDonationKnown
                && frame.TempleDonationRevision > previous.TempleDonationRevision;
            Sessions[peer] = new TownServiceSessionInfo { Peer = peer, PublicClaim = frame.PublicClaim, Service = frame.Service, Session = frame.Session,
                Sequence = frame.Sequence, SampleTime = frame.SampleTime, ReceivedTime = Time.unscaledTime, LastSeenTime = Time.unscaledTime, Active = frame.Visible,
                SessionAge = frame.SessionAge, TempleDonationKnown = frame.TempleDonationKnown,
                TempleDonationAvailable = frame.TempleDonationAvailable, TempleDonationRevision = frame.TempleDonationRevision,
                HasTempleDonationCommitAge = frame.HasTempleDonationCommitAge,
                TempleDonationChangedTime = frame.HasTempleDonationCommitAge
                    ? Time.unscaledTime - frame.TempleDonationCommitAge
                    : donationAdvanced ? Time.unscaledTime : previous?.TempleDonationChangedTime ?? 0f,
                TransactionActive = frame.TransactionActive,
                Modules = frame.Modules, Position = Position(frame.Pose), Rotation = Rotation(frame.Pose), Scale = Scale(frame.Pose) };
            if (peer > 0) VisitorSessions[peer] = Sessions[peer];
            if (peer > 0 && frame.Service == 2 && frame.TempleDonationKnown
                && frame.HasTempleDonationCommitAge && frame.TempleDonationRevision != 0)
                ObserveTempleDonationCommit(peer, frame.Session, frame.TempleDonationRevision,
                    frame.TempleDonationCommitAge);
            if (peer > 0) InteractionOwner(frame.Service); // start/advance the bounded claim window
            StagePreviousPublicPicture();
            if (!frame.Visible) ClearRemoteModules(peer);
            else if (Remote.TryGetValue(peer, out Dictionary<ushort, RemoteModule>? standing))
            {
                var removed = new List<ushort>();
                foreach (var pair in standing) if (Array.BinarySearch(frame.Modules, pair.Key) < 0 && !RackRetains(peer, pair.Key)) removed.Add(pair.Key);
                if (removed.Count == 0 || !StagePublicPicture(peer))
                    foreach (ushort id in removed) { standing[id].Dispose(); standing.Remove(id); }
            }
            PrunePending(Pending, peer, frame);
            PrunePending(ReceivedBaselines, peer, frame);
            ReconcileMerchantOffering(peer);
            if (peer > 0) FlushVoicePending(peer);
            else if (IsStockPeerKey(peer)) FlushStockVoicePending(RealPeer(peer));
            return true;
        }
        if (!Pending.TryGetValue(peer, out Dictionary<ushort, TownServiceFrame>? pending))
        {
            if (Pending.Count >= 24) return true;
            pending = new Dictionary<ushort, TownServiceFrame>(); Pending.Add(peer, pending);
        }
        if (Sessions.TryGetValue(peer, out TownServiceSessionInfo? live) && live.Active
            && live.Session == frame.Session && live.Service == frame.Service && Array.BinarySearch(live.Modules, frame.Module) >= 0)
            live.LastSeenTime = Time.unscaledTime;
        if (pending.Count >= TownServiceFrame.MaxModules && !pending.ContainsKey(frame.Module)) return true;
        // A keyframe may finish after a newer delta. Keep it even though its sample is older:
        // the waiting cumulative delta names this exact baseline and then becomes usable.
        if (frame.BaseSequence == 0)
        {
            if (!ReceivedBaselines.TryGetValue(peer, out Dictionary<ushort, TownServiceFrame>? baselines))
            { baselines = new Dictionary<ushort, TownServiceFrame>(); ReceivedBaselines.Add(peer, baselines); }
            if (!baselines.TryGetValue(frame.Module, out TownServiceFrame? before) || frame.Sequence > before.Sequence)
                baselines[frame.Module] = frame;
        }
        if (!pending.TryGetValue(frame.Module, out TownServiceFrame? old) || frame.Sequence > old.Sequence)
            { StageChangingPublicRack(peer, frame); pending[frame.Module] = frame; ObserveMerchantOffering(peer, frame); }
        if (IsStockPeerKey(peer)) FlushStockVoicePending(RealPeer(peer));
        return true;
    }

    private static void PrunePending(Dictionary<int, Dictionary<ushort, TownServiceFrame>> store, int peer, TownServiceFrame manifest)
    {
        if (!store.TryGetValue(peer, out Dictionary<ushort, TownServiceFrame>? modules)) return;
        var removed = new List<ushort>();
        foreach (var pair in modules)
            // Independent module lanes can overtake an older census/close packet. Keep
            // newer complete baselines until their own census arrives; TickRemote still
            // gates rendering by the current session and module membership.
            if (!RackRetains(peer, pair.Key) && pair.Value.Sequence <= manifest.Sequence && (!manifest.Visible
                || pair.Value.Service != manifest.Service || pair.Value.Session != manifest.Session
                || Array.BinarySearch(manifest.Modules, pair.Key) < 0)) removed.Add(pair.Key);
        foreach (ushort module in removed) modules.Remove(module);
    }

    /// <summary>Shared frame is supplied by the room owner. No observer-local fit or gaze pose is consulted.</summary>
    internal static void TickRemote(Func<int, Transform?> sharedFrame)
    {
        float now = Time.unscaledTime;
        StagePreviousPublicPicture();
        foreach (var entry in Sessions)
        {
            TownServiceSessionInfo session = entry.Value;
            bool stockVisitor = IsStockPeerKey(entry.Key);
            float staleSeconds = entry.Key > 0 || stockVisitor ? NetProtocol.StaleTimeoutSeconds : 10f;
            if (!session.Active || now - session.LastSeenTime > staleSeconds)
            { ClearRemoteModules(entry.Key); session.Active = false; continue; }
            // The NPC has one physical rack/workspace, not a separate copy per visitor.
            // Browsing input stays independent, while one elected presentation author
            // supplies the shared native widgets. A placed offer takes authorship.
            bool electedVisitor = entry.Key > 0 && InteractionOwner(session.Service) == entry.Key;
            bool secondaryVisitor = entry.Key > 0 && !electedVisitor
                && (session.Service == 1 || session.Service == 2 || session.Service == 3);
            if (entry.Key > 0 && !electedVisitor && !secondaryVisitor)
            { ClearRemoteModules(entry.Key); continue; }
            if (entry.Key < 0 && !stockVisitor && -entry.Key != PublicAuthor)
            { ClearRemoteModules(entry.Key); continue; }
            Transform? parent = sharedFrame(RealPeer(entry.Key));
            if (parent == null || !Pending.TryGetValue(entry.Key, out Dictionary<ushort, TownServiceFrame>? pending)) continue;
            if (!Remote.TryGetValue(entry.Key, out Dictionary<ushort, RemoteModule>? standing))
            { standing = new Dictionary<ushort, RemoteModule>(); Remote.Add(entry.Key, standing); }
            if (secondaryVisitor) RetainIndependentVisitorOnly(entry.Key, standing, session);
            else if (entry.Key > 0 && session.Service == 1) RetirePrivateMerchantCatalog(standing);
            if (entry.Key > 0 && session.Service == 3) RetireNonCanonicalMageCue(entry.Key, standing);
            // Unity destroys child GameObjects when their old module parent is retired.
            // An unchanged child packet must then rebuild its observer clone, not keep
            // a C# module whose native Host has been destroyed. The Build 587 peer
            // otherwise threw from rack SetActive every frame and skipped ApplyPending.
            RetireDestroyedRemoteModules(entry.Key, standing);
            foreach (RemoteModule visible in standing.Values)
            { visible.Motion.Tick(now); visible.Binding.TickAnimation(now); }
            if (!secondaryVisitor && !stockVisitor) UpdateRackClocks(entry.Key, pending, now);
            bool reorder = false;
            foreach (var packet in pending)
            {
                TownServiceFrame received = packet.Value;
                if (stockVisitor && !StockModule(received.TemplateAddress)) continue;
                // A cumulative delta need not repeat the purse's Mesh property.
                // Classify that original body only after expansion; a row/image
                // with the same address is still not a second shared bowl cue.
                if (received.Service == 3 && received.TemplateAddress == "merchant.zone|"
                    && entry.Key != TownServiceSharedCue.GuideOwner) continue;
                if (secondaryVisitor && !IndependentVisitorModule(received, entry.Key, !session.TransactionActive)
                    && !(received.Service == 2 && received.TemplateAddress == "ritual.purse|")) continue;
                if (entry.Key > 0 && PrivateMerchantCatalogModule(received.Service,
                    received.TemplateAddress, received.ParentModule)) continue;
                long retryKey = ((long)entry.Key << 16) | received.Module;
                if (RemoteRetry.TryGetValue(retryKey, out float retryAt) && now < retryAt) continue;
                standing.TryGetValue(received.Module, out RemoteModule? module);
                if (module != null && received.Sequence <= module.Sequence) continue;
                TownServiceFrame? baseline = null;
                if (ReceivedBaselines.TryGetValue(entry.Key, out Dictionary<ushort, TownServiceFrame>? baselines)) baselines.TryGetValue(received.Module, out baseline);
                TownServiceFrame? expanded = TownServiceDelta.Expand(baseline, received);
                if (expanded == null) continue;
                TownServiceFrame frame = expanded;
                if (secondaryVisitor && !IndependentVisitorModule(frame, entry.Key, !session.TransactionActive)) continue;
                if (frame.Session != session.Session || frame.Service != session.Service || Array.BinarySearch(session.Modules, frame.Module) < 0) continue;
                try
                {
                    if (!frame.Visible)
                    { if (module != null) { module.Host.SetActive(false); module.Motion.Reset(); module.Sequence = frame.Sequence; module.LastFrame = frame; } continue; }
                    Transform mount = parent;
                    if (frame.ParentModule != TownServiceFrame.ManifestModule)
                    {
                        if (!standing.TryGetValue(frame.ParentModule, out RemoteModule? parentModule)) continue;
                        if (!parentModule.Alive) continue;
                        int index = Array.IndexOf(parentModule.Binding.Bindings, frame.ParentBinding);
                        if (index < 0) throw new InvalidDataException("Original town-service parent binding is absent.");
                        mount = parentModule.Binding.Nodes[index];
                        if (module != null && mount.IsChildOf(module.Host.transform))
                            throw new InvalidDataException("Cyclic town-service module parent.");
                    }
                    if (module == null || module.Template != frame.Template || module.Address != frame.TemplateAddress || module.Session != frame.Session
                        || (module.AddedCanvas != null) != NeedsCanvas(frame))
                    {
                        RemoteModule candidate = BuildRemote(frame, mount);
                        try { candidate.Binding.Validate(frame, Assets); candidate.Binding.Apply(frame, Assets); }
                        catch (InvalidDataException e) when (e.Message == "Original town-service template differs between peers.")
                        {
                            string detail = DescribeTemplateMismatch(frame, candidate.Binding);
                            candidate.Dispose();
                            throw new InvalidDataException(detail, e);
                        }
                        catch { candidate.Dispose(); throw; }
                        if (module != null)
                        {
                            // Replacing this host also destroys every module physically
                            // mounted under it. Retire those entries before the next
                            // rack/page pass so their retained owner frames rebuild them.
                            RetireRemoteDescendants(standing, frame.Module, module);
                            module.Dispose();
                        }
                        module = candidate; standing[frame.Module] = module;
                    }
                    else
                    {
                        // Resolve all assets before a tween or binding can change visible content.
                        module.Binding.Validate(frame, Assets);
                        if (!module.Host.activeSelf || module.LastFrame?.ParentModule != frame.ParentModule || module.LastFrame?.ParentBinding != frame.ParentBinding)
                            module.Motion.Reset();
                        module.Motion.BeforeApply(now); module.Binding.Apply(frame, Assets);
                    }
                    Transform root = module.Binding.Root;
                    module.Host.transform.SetParent(mount, false);
                    if (frame.ParentModule != TownServiceFrame.ManifestModule
                        && frame.Nodes[0].Values.TryGetValue(TownServiceProperty.Sibling, out TownServiceValue? sibling))
                        module.Host.transform.SetSiblingIndex((int)sibling.Numbers[0]);
                    module.Host.GetComponent<CanvasGroup>().alpha = frame.ParentAlpha;
                    if (frame.HasCanvasFrame && module.AddedCanvas != null)
                    {
                        ApplyCanvasFrame(module, frame, parent);
                    }
                    Transform poseRoot = module.AddedCanvas != null && !frame.HasCanvasFrame ? module.Host.transform : root;
                    poseRoot.position = mount.TransformPoint(Position(frame.Pose)); poseRoot.rotation = mount.rotation * Rotation(frame.Pose);
                    Vector3 worldScale = Vector3.Scale(mount.lossyScale, Scale(frame.Pose)), parentScale = poseRoot.parent.lossyScale;
                    poseRoot.localScale = new Vector3(worldScale.x / parentScale.x, worldScale.y / parentScale.y, worldScale.z / parentScale.z);
                    if (module.AddedCanvas != null && !frame.HasCanvasFrame)
                    {
                        if (root is RectTransform rr && module.Host.transform is RectTransform hostRect)
                        { hostRect.pivot = rr.pivot; hostRect.sizeDelta = rr.rect.size; }
                        root.localPosition = Vector3.zero; root.localRotation = Quaternion.identity; root.localScale = Vector3.one;
                    }
                    if (module.LastFrame != null && module.Host.activeSelf && module.LastFrame.ParentModule == frame.ParentModule
                        && module.LastFrame.ParentBinding == frame.ParentBinding)
                        module.Motion.AfterApply(now, frame.SampleTime - module.LastFrame.SampleTime);
                    RemoteRetry.Remove(retryKey); module.Sequence = frame.Sequence; module.LastFrame = frame; reorder = true;
                    module.Host.SetActive(true); root.gameObject.SetActive(true);
                    FinishInertPresentation?.Invoke(frame.TemplateAddress, root, parent);
                    TownServiceDepthOrder.Refresh(module.Host.transform);
                }
                catch (Exception e)
                {
                    if (module != null)
                    {
                        // Failure cleanup must never throw from the same destroyed
                        // Unity object; that secondary exception used to escape the
                        // per-module guard and abort every other network presentation.
                        // Validation resolves every original asset BEFORE changing the
                        // picture. A transient missing dependency may not turn an already
                        // validated item grey or erase a complete cabinet page. Retain only
                        // this exact original/session/structure; a changed card identity or
                        // hierarchy is a new module and cannot borrow the preceding artwork.
                        if (module.Alive)
                        {
                            bool sameOriginal = module.LastFrame != null
                                && module.Session == frame.Session && module.Template == frame.Template
                                && module.Address == frame.TemplateAddress && module.Binding.Structure == frame.Structure;
                            if (!sameOriginal) { module.Host.SetActive(false); module.Motion.Reset(); }
                        }
                        else { module.Dispose(); standing.Remove(frame.Module); }
                    }
                    if (RemoteRetry.Count >= 8 * TownServiceFrame.MaxModules && !RemoteRetry.ContainsKey(retryKey)) RemoteRetry.Clear();
                    RemoteRetry[retryKey] = now + .25f;
                    Exception report = e;
                    if (e is InvalidDataException mismatch && mismatch.Message == "Original town-service template differs between peers."
                        && module != null && module.Alive)
                        report = new InvalidDataException(DescribeTemplateMismatch(frame, module.Binding), mismatch);
                    Report("remote module " + entry.Key + "/" + frame.Module, report);
                }
            }
            if (reorder) OrderOriginalSiblings(standing);
            if (!secondaryVisitor && !stockVisitor) TickRackClocks(entry.Key, standing, now);
        }
        ApplyRemoteMotion(now);
        // Public visibility follows the latest validated numeric picture, not
        // the slower immutable-art baseline retained for dependency recovery.
        SuppressRemoteStockDuplicates();
        foreach (var visitor in VisitorSessions)
            if (visitor.Value.Active && visitor.Value.Service == 2
                && visitor.Key != InteractionOwner(2) && Remote.TryGetValue(visitor.Key, out var temple))
                SetSecondaryTempleInscriptions(visitor.Value, temple);
        foreach (var visitor in Remote)
        {
            if (visitor.Key != TownServiceSharedCue.GuideOwner) continue;
            foreach (RemoteModule cue in visitor.Value.Values)
                if (cue.Alive && cue.LastFrame?.Service == 3 && cue.Address == "merchant.zone|")
                    TownServiceSharedCue.PaintRemote(cue.Host.GetComponent<CanvasGroup>(), cue.Binding.Root);
        }
        CommitPublicPicture();
    }

    private static string DescribeTemplateMismatch(TownServiceFrame frame, TownServiceBinding local)
    {
        int common = Math.Min(frame.Nodes.Length, local.Bindings.Length), first = -1;
        for (int i = 0; i < common; i++)
            if (frame.Nodes[i].Binding != local.Bindings[i]) { first = i; break; }
        string binding = first < 0 ? "same prefix"
            : "first binding " + first + " sender=" + frame.Nodes[first].Binding.ToString("X8")
                + " observer=" + local.Bindings[first].ToString("X8") + " (" + local.Nodes[first].name + ")";
        // One bounded anomaly report now identifies whether this is a native
        // cross-install template difference or a transient local hierarchy
        // mutation. Never admit a mismatched tree: that would apply original
        // card/text values to the wrong observer nodes.
        return "Original town-service template differs between peers: " + frame.TemplateAddress
            + ", sender structure=" + frame.Structure.ToString("X8") + "/" + frame.Nodes.Length
            + ", observer structure=" + local.Structure.ToString("X8") + "/" + local.Bindings.Length
            + ", " + binding + ".";
    }

    // The stand and original UI have one elected author. Personal fan/held props,
    // released card flights and the one elected mage guide remain independent of
    // that UI lease. No native callback is mirrored on any of these copies.
    private static bool IndependentVisitorModule(TownServiceFrame frame, int peer = 0, bool returning = false) =>
        IndependentVisitorModule(frame.Service, frame.TemplateAddress, frame.ParentModule, peer, returning)
        || frame.Service == 2 && frame.TemplateAddress == "ritual.purse|" && PhysicalPurse(frame.Nodes);
    private static bool PhysicalPurse(TownServiceNode[] nodes)
    {
        foreach (TownServiceNode node in nodes)
            if (node.Values.ContainsKey(TownServiceProperty.Mesh)) return true;
        return false;
    }
    private static bool IndependentVisitorModule(byte service, string address, ushort parentModule,
        int peer = 0, bool returning = false)
    {
        if (service == 2)
            return address.StartsWith("ritual.purse.held|", StringComparison.Ordinal)
                || address.StartsWith("temple.row|", StringComparison.Ordinal);
        if (service == 3)
        {
            // Only the explicitly approved shared guide has a separate visual
            // author. Native controls and a parked card still follow the grant.
            if (address == "merchant.zone|") return peer > 0 && peer == TownServiceSharedCue.GuideOwner;
            // A released face/body can still be flying to its owner's ordinary
            // map fan while a different visitor is already using the enchantress.
            return returning && (address.StartsWith("face.", StringComparison.Ordinal)
                || address.StartsWith("map.cardbody|", StringComparison.Ordinal));
        }
        if (service != 1) return false;
        // The public cabinet has a separate elected lane. A visitor's original
        // item fan and physically held card remain visible even while another
        // visitor authors the merchant's shared palm and purchase controls.
        // Cabinet cards have a published cardmount parent; the fan has none.
        if (address.StartsWith("inspectionbody.", StringComparison.Ordinal)) return true;
        if (!address.StartsWith("item.", StringComparison.Ordinal)
            || address.StartsWith("item.confirm", StringComparison.Ordinal)) return false;
        int end = address.IndexOf('|');
        if (end <= 5) return false;
        for (int i = 5; i < end; i++) if (address[i] < '0' || address[i] > '9') return false;
        // Partitioned children of the same original face retain a parent module.
        // A cabinet's root face has a physical cardmount parent and is excluded;
        // its children cannot build without that parent. Personal fan root faces
        // and their native partitions must all survive another visitor's lease.
        return parentModule == TownServiceFrame.ManifestModule || end + 1 < address.Length;
    }

    private static readonly List<ushort> SecondaryVisitorRetire = new();
    private static void RetainIndependentVisitorOnly(int peer, Dictionary<ushort, RemoteModule> modules,
        TownServiceSessionInfo session)
    {
        SecondaryVisitorRetire.Clear();
        foreach (var pair in modules)
            if (!(pair.Value.LastFrame != null ? IndependentVisitorModule(pair.Value.LastFrame, peer, !session.TransactionActive)
                : IndependentVisitorModule(session.Service, pair.Value.Address, TownServiceFrame.ManifestModule, peer, !session.TransactionActive)))
                SecondaryVisitorRetire.Add(pair.Key);
        foreach (ushort id in SecondaryVisitorRetire) { modules[id].Dispose(); modules.Remove(id); }
    }

    private static void RetireNonCanonicalMageCue(int peer, Dictionary<ushort, RemoteModule> modules)
    {
        if (peer == TownServiceSharedCue.GuideOwner) return;
        SecondaryVisitorRetire.Clear();
        foreach (var pair in modules)
            if (pair.Value.Address == "merchant.zone|") SecondaryVisitorRetire.Add(pair.Key);
        foreach (ushort id in SecondaryVisitorRetire) { modules[id].Dispose(); modules.Remove(id); }
    }

    private static bool PrivateMerchantCatalogModule(byte service, string address, ushort parentModule)
    {
        if (service != 1) return false;
        if (address.StartsWith("item.", StringComparison.Ordinal)
            && !address.StartsWith("item.confirm", StringComparison.Ordinal)
            && parentModule != TownServiceFrame.ManifestModule && address.EndsWith("|", StringComparison.Ordinal)) return true;
        return address.StartsWith("merchant.cardmount|", StringComparison.Ordinal)
            || address.StartsWith("merchant.cardbody|", StringComparison.Ordinal)
            || address.StartsWith("merchant.row|", StringComparison.Ordinal)
            || address.StartsWith("merchant.rack|", StringComparison.Ordinal)
            || address.StartsWith("merchant.crank|", StringComparison.Ordinal)
            || address.StartsWith("merchant.category.", StringComparison.Ordinal)
            || address.StartsWith("merchant.counter|", StringComparison.Ordinal)
            || address.StartsWith("merchant.return|", StringComparison.Ordinal);
    }

    private static void RetirePrivateMerchantCatalog(Dictionary<ushort, RemoteModule> modules)
    {
        SecondaryVisitorRetire.Clear();
        foreach (var pair in modules)
            if (PrivateMerchantCatalogModule(1, pair.Value.Address,
                    pair.Value.LastFrame?.ParentModule ?? TownServiceFrame.ManifestModule))
                SecondaryVisitorRetire.Add(pair.Key);
        foreach (ushort id in SecondaryVisitorRetire) { modules[id].Dispose(); modules.Remove(id); }
    }

    private readonly struct SiblingRank
    { internal readonly Transform Node; internal readonly int Index;
        internal SiblingRank(Transform node, int index) { Node = node; Index = index; } }
    private static readonly Dictionary<Transform, List<SiblingRank>> OrderGroups = new();
    private static readonly List<List<SiblingRank>> OrderLists = new();
    private static void OrderOriginalSiblings(Dictionary<ushort, RemoteModule> modules)
    {
        foreach (RemoteModule module in modules.Values)
        {
            TownServiceFrame? frame = module.LastFrame; if (frame == null) continue;
            if (frame.ParentModule != TownServiceFrame.ManifestModule && frame.Nodes[0].Values.TryGetValue(TownServiceProperty.Sibling, out TownServiceValue? rootOrder))
                AddOrder(module.Host.transform, (int)rootOrder.Numbers[0]);
            for (int i = 1; i < frame.Nodes.Length; i++)
                if (frame.Nodes[i].Values.TryGetValue(TownServiceProperty.Sibling, out TownServiceValue? order))
                    AddOrder(module.Binding.Nodes[i], (int)order.Numbers[0]);
        }
        foreach (List<SiblingRank> group in OrderGroups.Values)
        {
            group.Sort((a, b) => a.Index.CompareTo(b.Index));
            for (int i = 0; i < group.Count; i++) group[i].Node.SetSiblingIndex(i);
            group.Clear();
        }
        OrderGroups.Clear();
    }
    private static void AddOrder(Transform node, int index)
    {
        if (node.parent == null) return;
        if (!OrderGroups.TryGetValue(node.parent, out List<SiblingRank>? list))
        {
            int next = OrderGroups.Count;
            if (next == OrderLists.Count) OrderLists.Add(new List<SiblingRank>());
            list = OrderLists[next]; OrderGroups.Add(node.parent, list);
        }
        list.Add(new SiblingRank(node, index));
    }
    private static RemoteModule BuildRemote(TownServiceFrame frame, Transform sharedFrame)
    {
        string key = TemplateKey(frame.Service, frame.Template, frame.TemplateAddress);
        if (!Templates.ContainsKey(key)) ResolveTemplate?.Invoke(frame.Service, frame.Template, frame.TemplateAddress);
        if (!Templates.TryGetValue(key, out GameObject? template) || template == null)
            throw new InvalidDataException("Original town-service template is not registered on this client.");
        var host = new GameObject("GloomhavenVR.TownService.Observer", typeof(RectTransform));
        host.SetActive(false); host.transform.SetParent(sharedFrame, false);
        Canvas? canvas = null;
        if (NeedsCanvas(frame))
        {
            canvas = host.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Rig.VRRigDriver.HeadCamera != null ? Rig.VRRigDriver.HeadCamera : Camera.main;
        }
        CanvasGroup group = host.AddComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
        try
        {
            GameObject clone = Object.Instantiate(template, host.transform, false);
            // Templates are already inert; repeat the invariant before the clone can become active.
            TownServiceNeutralize.Apply(clone);
            PrepareInertGeometry?.Invoke(frame.TemplateAddress, clone);
            VRLayers.Apply(host);
            foreach (Canvas originalCanvas in clone.GetComponentsInChildren<Canvas>(true))
                originalCanvas.worldCamera = Rig.VRRigDriver.HeadCamera != null ? Rig.VRRigDriver.HeadCamera : Camera.main;
            var binding = new TownServiceBinding(clone.transform);
            var module = new RemoteModule { Host = host, AddedCanvas = canvas, Binding = binding,
                Motion = new TownServiceMotion(host.transform, binding.Nodes, frame.TemplateAddress), Session = frame.Session,
                Template = frame.Template, Address = frame.TemplateAddress };
            TownServiceDepthOrder.Bind(host.transform);
            return module;
        }
        catch
        {
            Object.Destroy(host);
            throw;
        }
    }

    private static bool NeedsCanvas(TownServiceFrame frame) => frame.HasCanvasFrame
        || frame.ParentModule == TownServiceFrame.ManifestModule && !frame.Nodes[0].Values.ContainsKey(TownServiceProperty.Canvas);
    private static void ApplyCanvasFrame(RemoteModule module, TownServiceFrame frame, Transform shared)
    {
        Transform host = module.Host.transform;
        host.position = shared.TransformPoint(Position(frame.CanvasPose)); host.rotation = shared.rotation * Rotation(frame.CanvasPose);
        Vector3 desired = Vector3.Scale(shared.lossyScale, Scale(frame.CanvasPose)), basis = host.parent.lossyScale;
        host.localScale = new Vector3(desired.x / basis.x, desired.y / basis.y, desired.z / basis.z);
        RectTransform rect = (RectTransform)host; rect.sizeDelta = new Vector2(frame.CanvasRect[0], frame.CanvasRect[1]);
        rect.pivot = new Vector2(frame.CanvasRect[2], frame.CanvasRect[3]);
        Canvas canvas = module.AddedCanvas!;
        canvas.referencePixelsPerUnit = frame.CanvasSettings[0]; canvas.pixelPerfect = frame.CanvasSettings[1] != 0;
        canvas.overridePixelPerfect = frame.CanvasSettings[2] != 0; canvas.scaleFactor = frame.CanvasSettings[3];
        canvas.additionalShaderChannels = (AdditionalCanvasShaderChannels)(int)frame.CanvasSettings[4];
        canvas.sortingOrder = frame.CanvasSortingOrder; canvas.sortingLayerID = frame.CanvasSortingLayer;
    }
    internal static void RemovePeer(int peer)
    { ForgetPublicPicture(-peer); MerchantOfferings.Remove(peer); ClearVoicePeer(peer); ClearRemoteModules(peer); Pending.Remove(peer); ReceivedBaselines.Remove(peer); Sessions.Remove(peer); VisitorSessions.Remove(peer);
      ClearRemoteModules(-peer); Pending.Remove(-peer); ReceivedBaselines.Remove(-peer); Sessions.Remove(-peer);
      RemoveStockPeer(peer); ForgetDonationCommits(peer); ForgetRemoteMotion(peer); }
    private static void ForgetDonationCommits(int peer)
    {
        int count = DonationCommitOrder.Count;
        for (int i = 0; i < count; i++)
        {
            var key = DonationCommitOrder.Dequeue();
            if (key.Peer == peer) DonationCommits.Remove(key);
            else DonationCommitOrder.Enqueue(key);
        }
    }
    internal static void RequestFullRefresh()
    {
        foreach (LocalModule module in AllLocalModules())
        { module.Last = null; module.Baseline = null; module.NextRefresh = module.NextBaseline = 0; }
        PrivateLane.NextManifest = PublicLane.NextManifest = StockLane.NextManifest = 0;
    }
    private static IEnumerable<LocalModule> AllLocalModules()
    { foreach (LocalModule module in PrivateLane.Modules.Values) yield return module;
      foreach (LocalModule module in PublicLane.Modules.Values) yield return module;
      foreach (LocalModule module in StockLane.Modules.Values) yield return module; }
    internal static void ResetNetwork()
    {
        ResetMotionNetwork();
        ResetPublicPicture();
        DonationCommits.Clear(); DonationCommitOrder.Clear();
        if (PrivateLane.TransactionActive)
            TownServiceGrantSync.SetOffer(PrivateLane.Service, PrivateLane.Session, false);
        foreach (int peer in new List<int>(Remote.Keys)) ClearRemoteModules(peer);
        MerchantOfferings.Clear(); ClearVoiceNetwork(); Pending.Clear(); ReceivedBaselines.Clear(); Sessions.Clear(); VisitorSessions.Clear(); RemoteRetry.Clear(); foreach (LocalModule module in AllLocalModules())
        { module.Last = null; module.Baseline = null; module.NextRefresh = module.NextBaseline = 0; }
        PrivateLane.NextManifest = PublicLane.NextManifest = StockLane.NextManifest = 0;
        PrivateLane.TempleDonationKnown = PrivateLane.TempleDonationAvailable = false;
        PrivateLane.TempleDonationRevision = 0; PrivateLane.TempleDonationChangedTime = 0f;
        PrivateLane.TransactionActive = false;
        ResetInteractionLeases(); ClearStockPeerKeys();
    }
    internal static void Shutdown()
    {
        ResetNetwork(); using (new LaneScope(StockLane)) { ClearLocalModules(); _session = 0; _active = false; }
        using (new LaneScope(PublicLane)) { ClearLocalModules(); _session = 0; _active = false; }
        ClearLocalModules(); Templates.Clear();
        if (_templateHost != null) Object.Destroy(_templateHost);
        _templateHost = null; _session = 0; _service = 0; _active = false; _station = _sharedFrame = null; ClearVoiceOutgoing();
        PrivateLane.TempleDonationKnown = PrivateLane.TempleDonationAvailable = false;
        PrivateLane.TempleDonationRevision = 0; PrivateLane.TempleDonationChangedTime = 0f;
        PrivateLane.TransactionActive = false;
        ResetInteractionLeases();
        SourceParents.Clear(); ParentGroups.Clear(); TownServiceMaterial.Reset(); Assets.Clear();
        ReportReset();
    }
    private static void ClearLocalModules()
    { SourceParents.Clear(); LocalRacks.Clear(); LocalRackMembers.Clear(); LocalRackGates.Clear(); foreach (LocalModule module in Local.Values) module.Binding.Dispose(); Local.Clear(); }
    private static void ResetInteractionLeases()
    {
        for (int i = 1; i < InteractionLeases.Length; i++)
        { InteractionLeases[i].Player = 0; InteractionLeases[i].Session = 0; InteractionLeases[i].PendingSince = float.NegativeInfinity;
          TransactionLeases[i].Player = 0; TransactionLeases[i].Session = 0; TransactionLeases[i].PendingSince = float.NegativeInfinity; }
    }
    private static void ClearRemoteModules(int peer)
    { RemoteRacks.Remove(peer); if (!Remote.TryGetValue(peer, out Dictionary<ushort, RemoteModule>? modules)) return;
        foreach (RemoteModule module in modules.Values) module.Dispose(); Remote.Remove(peer); }
    private static string TemplateKey(byte service, ushort template, string address = "")
    { if (service < 1 || service > 3 || template == 0) throw new ArgumentException("Invalid town-service template identity.");
        return service + ":" + (address.Length == 0 ? template.ToString() : address); }
    private static ulong NextSequence() { if (++_sequence == 0) ++_sequence; return _sequence; }
    private static float[] ReadPose(Transform source, Transform frame, float[]? destination = null)
    {
        Vector3 p = frame.InverseTransformPoint(source.position), s = source.lossyScale, basis = frame.lossyScale;
        Quaternion q = Quaternion.Inverse(frame.rotation) * source.rotation;
        float[] result = destination ?? new float[10];
        result[0]=p.x; result[1]=p.y; result[2]=p.z; result[3]=q.x; result[4]=q.y; result[5]=q.z; result[6]=q.w;
        result[7]=s.x/basis.x; result[8]=s.y/basis.y; result[9]=s.z/basis.z;
        return result;
    }
    private static float[] IdentityPose() => new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f };
    private static Vector3 Position(float[] pose) => new(pose[0], pose[1], pose[2]);
    private static Quaternion Rotation(float[] pose) => new(pose[3], pose[4], pose[5], pose[6]);
    private static Vector3 Scale(float[] pose) => new(pose[7], pose[8], pose[9]);
    private static bool SamePresentation(TownServiceFrame? a, TownServiceFrame b)
    {
        if ((a?.RackMember == null) != (b.RackMember == null) || a?.RackMember != null && !a.RackMember.Same(b.RackMember)) return false;
        if (a != null && (a.VisitorStock != b.VisitorStock || a.PublicCatalog != b.PublicCatalog || a.PublicClaim != b.PublicClaim)) return false;
        if ((a?.Rack == null) != (b.Rack == null) || a?.Rack != null && !a.Rack.Same(b.Rack)) return false;
        if (a == null || a.Visible != b.Visible || a.Structure != b.Structure || a.Nodes.Length != b.Nodes.Length
            || a.ParentModule != b.ParentModule || a.ParentBinding != b.ParentBinding || a.ParentAlpha != b.ParentAlpha
            || a.HasCanvasFrame != b.HasCanvasFrame || a.CanvasSortingLayer != b.CanvasSortingLayer || a.CanvasSortingOrder != b.CanvasSortingOrder) return false;
        for (int i = 0; i < 10; i++) if (a.Pose[i] != b.Pose[i] || a.CanvasPose[i] != b.CanvasPose[i]) return false;
        for (int i = 0; i < 4; i++) if (a.CanvasRect[i] != b.CanvasRect[i]) return false;
        for (int i = 0; i < 5; i++) if (a.CanvasSettings[i] != b.CanvasSettings[i]) return false;
        for (int i = 0; i < a.Nodes.Length; i++)
        {
            if (ReferenceEquals(a.Nodes[i], b.Nodes[i])) continue;
            if (a.Nodes[i].Binding != b.Nodes[i].Binding || a.Nodes[i].Values.Count != b.Nodes[i].Values.Count) return false;
            foreach (var pair in a.Nodes[i].Values)
                if (!b.Nodes[i].Values.TryGetValue(pair.Key, out TownServiceValue? value) || !pair.Value.Same(value)) return false;
        }
        return true;
    }
    private static void ReadCanvasFrame(Transform source, TownServiceFrame frame)
    {
        if (frame.ParentModule != TownServiceFrame.ManifestModule || _sharedFrame == null) return;
        source.GetComponentsInParent(true, ParentCanvases);
        Canvas? canvas = null;
        foreach (Canvas candidate in ParentCanvases) if (candidate.isActiveAndEnabled) { canvas = candidate.rootCanvas; break; }
        if (canvas == null || canvas.transform == source || canvas.transform is not RectTransform rect) return;
        frame.HasCanvasFrame = true; frame.CanvasPose = ReadPose(canvas.transform, _sharedFrame, frame.CanvasPose);
        frame.CanvasRect[0]=rect.rect.width; frame.CanvasRect[1]=rect.rect.height; frame.CanvasRect[2]=rect.pivot.x; frame.CanvasRect[3]=rect.pivot.y;
        frame.CanvasSettings[0]=canvas.referencePixelsPerUnit; frame.CanvasSettings[1]=canvas.pixelPerfect?1f:0f;
        frame.CanvasSettings[2]=canvas.overridePixelPerfect?1f:0f; frame.CanvasSettings[3]=canvas.scaleFactor; frame.CanvasSettings[4]=(float)canvas.additionalShaderChannels;
        frame.CanvasSortingLayer = canvas.sortingLayerID; frame.CanvasSortingOrder = canvas.sortingOrder;
    }
    private static void ResetCanvasFrame(TownServiceFrame frame)
    {
        frame.HasCanvasFrame=false; frame.CanvasSortingLayer=frame.CanvasSortingOrder=0;
        Array.Clear(frame.CanvasPose,0,frame.CanvasPose.Length);
        frame.CanvasPose[6]=frame.CanvasPose[7]=frame.CanvasPose[8]=frame.CanvasPose[9]=1f;
        frame.CanvasRect[0]=frame.CanvasRect[1]=100f; frame.CanvasRect[2]=frame.CanvasRect[3]=.5f;
        frame.CanvasSettings[0]=100f;frame.CanvasSettings[1]=frame.CanvasSettings[2]=frame.CanvasSettings[4]=0f;frame.CanvasSettings[3]=1f;
    }
    private static void ReadParent(LocalModule module, TownServiceFrame frame)
    {
        float alpha = 1;
        for (Transform? parent = module.Binding.Root.parent; parent != null; parent = parent.parent)
        {
            if (SourceParents.TryGetValue(parent, out ParentLink link) && link.Module != module.Id)
            {
                frame.ParentModule = link.Module; frame.ParentBinding = link.Binding;
                frame.ParentAlpha = alpha; frame.Pose = ReadPose(module.Binding.Root, parent, frame.Pose); return;
            }
            bool stop = false;
            parent.GetComponents(ParentGroups);
            foreach (CanvasGroup group in ParentGroups)
                if (group.enabled) { alpha *= group.alpha; if (group.ignoreParentGroups) stop = true; }
            if (stop) break;
        }
        frame.ParentAlpha = alpha;
    }
    private static void ReportReset()
    {
        Failures.Clear(); _reportWindow = 0; _reportCount = 0;
    }
    private static void Report(string phase, Exception error)
    {
        string key = phase + ": " + error.Message; float now = Time.unscaledTime;
        if (now - _reportWindow >= 30f) { _reportWindow = now; _reportCount = 0; }
        if (_reportCount >= 8)
        {
            if (_reportCount == 8) { _reportCount++; VRLog.Note("TownServices", "Further presentation errors are suppressed for this 30-second interval."); }
            return;
        }
        if (Failures.TryGetValue(key, out float last) && now - last < 30) return;
        if (Failures.Count > 32) Failures.Clear();
        Failures[key] = now; _reportCount++;
        VRLog.Note("TownServices", "Original service presentation unavailable (" + key + ").");
        PresentationUnavailable?.Invoke(key);
    }
}
