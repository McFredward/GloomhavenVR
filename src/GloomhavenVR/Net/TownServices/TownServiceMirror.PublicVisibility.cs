using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    // Authorship is a gameplay input decision. Presentation commits only after
    // every original dependency of that author's current page is validated.
    // The previous inert native group stays intact until that atomic replacement;
    // its source is never republished and it contains no gameplay callbacks.
    private static Dictionary<ushort, RemoteModule>? RetainedPublic;
    private static int _displayedPublicPeer, _pendingPublicPeer, _retainedPublicPeer;
    private static uint _pendingPublicTurn;
    private static int _committedPublicAuthor;
    private static bool _committedPublicReady;
    internal static Action? CommitPublicVisibility;

    internal static bool HasReadyPublicPresentation
    {
        get
        {
            int peer = -PublicAuthor;
            if (peer == -LocalPeer || !Sessions.TryGetValue(peer, out var session)
                || !session.Active || Time.unscaledTime - session.LastSeenTime > 10f
                || !Remote.TryGetValue(peer, out var modules)
                || !RemoteRacks.TryGetValue(peer, out var clocks)) return false;
            foreach (var pair in clocks)
            {
                RackPlayback clock = pair.Value;
                if (!modules.TryGetValue(pair.Key, out var root) || !root.Alive
                    || root.LastFrame?.Rack == null || root.LastFrame.Session != session.Session
                    || root.LastFrame.PublicClaim != session.PublicClaim
                    || root.Sequence < clock.Sequence
                    || Array.BinarySearch(session.Modules, pair.Key) < 0
                    || EffectiveRemoteFrame(root)?.Visible != true) continue;
                TownRackState state = clock.Latest;
                if (peer == _pendingPublicPeer && state.Turn < _pendingPublicTurn) continue;
                float age = state.Elapsed + Mathf.Max(0f, Time.unscaledTime - clock.ReceivedTime);
                if (!PublicPageReady(pair.Key, state, state.To, session, modules)) continue;
                if (state.Turn != 0 && age < TownRackState.TurnDuration
                    && !PublicPageReady(pair.Key, state, state.From, session, modules)) continue;
                return true;
            }
            return false;
        }
    }

    private static bool PublicPageReady(ushort rack, TownRackState state, ushort page,
        TownServiceSessionInfo session, Dictionary<ushort, RemoteModule> modules)
    {
        if (!RackPageReady(rack, state, page, modules)) return false;
        foreach (TownRackMember member in state.Members)
        {
            if (member.Page != page || member.Detached) continue;
            RemoteModule module = modules[member.Id];
            if (module.Session != session.Session || module.LastFrame!.PublicClaim != session.PublicClaim
                || Array.BinarySearch(session.Modules, member.Id) < 0) return false;
            // Current publishers stamp each native mount, body, face and price
            // with the same page epoch. A preceding page's reusable module is
            // not evidence that this new original group has arrived.
            if (state.Layout != null && module.LastFrame.RackMember!.Turn < state.Turn) return false;
        }
        if (state.Layout == null) return true; // Historical TLV85 has no stock census.
        foreach (TownCatalogSlot slot in state.Layout)
        {
            int category = slot.Ordinal / TownCatalogLayout.SlotsPerCategory;
            int ordinal = slot.Ordinal % TownCatalogLayout.SlotsPerCategory;
            if (category * 256 + ordinal / 12 != page || StockHeldIds.Contains(slot.ItemId)) continue;
            bool found = false;
            foreach (TownRackMember member in state.Members)
            {
                if (member.Page != page || member.Detached || !modules.TryGetValue(member.Id, out var module)
                    || !TryStockItemId(module.Address, out int id) || id != slot.ItemId) continue;
                // A nested partition alone cannot satisfy the original face root.
                int split = module.Address.IndexOf('|');
                if (split == module.Address.Length - 1) { found = true; break; }
            }
            if (!found) return false;
        }
        return true;
    }

    private static void StagePreviousPublicPicture()
    {
        int elected = PublicAuthor;
        if (_displayedPublicPeer != 0 && _displayedPublicPeer != -elected)
            StagePublicPicture(_displayedPublicPeer);
        // A local press is immediate. The previous remote page must never cover
        // the new locally authored native cabinet while its proxy accepts input.
        if (elected == LocalPeer) ClearRetainedPublic();
    }

    private static void StageChangingPublicRack(int peer, TownServiceFrame received)
    {
        if (peer != _displayedPublicPeer || !Remote.TryGetValue(peer, out var modules)) return;
        foreach (RemoteModule root in modules.Values)
        {
            if (root.LastFrame?.Rack == null || received.Session != root.Session
                || received.PublicClaim != root.LastFrame.PublicClaim) continue;
            uint turn = received.Rack?.Turn ?? received.RackMember?.Turn ?? 0;
            if (turn <= root.LastFrame.Rack.Turn) continue;
            // A reordered member can precede its root clock. It still starts a
            // replacement epoch, but cannot reveal an old clock as "ready".
            if (StagePublicPicture(peer))
            { _pendingPublicPeer = peer; _pendingPublicTurn = turn; }
            return;
        }
    }

    private static bool StagePublicPicture(int peer)
    {
        if (peer >= 0 || IsStockPeerKey(peer) || peer != _displayedPublicPeer
            || !Remote.TryGetValue(peer, out var modules) || modules.Count == 0) return false;
        ClearRetainedPublic(); RetainedPublic = modules; _retainedPublicPeer = peer;
        Remote.Remove(peer); RemoteRacks.Remove(peer); _displayedPublicPeer = 0;
        foreach (RemoteModule module in modules.Values)
            if (module.Alive && (module.StockMasked || module.LastFrame?.RackMember?.Detached == true))
                module.Host.SetActive(false);
        return true;
    }

    private static void CommitPublicPicture()
    {
        int elected = PublicAuthor;
        bool ready = elected == LocalPeer || HasReadyPublicPresentation;
        if (elected != _committedPublicAuthor || ready != _committedPublicReady)
        {
            _committedPublicAuthor = elected; _committedPublicReady = ready;
            CommitPublicVisibility?.Invoke();
        }
        if (elected != LocalPeer && Remote.TryGetValue(-elected, out var candidate))
        {
            foreach (RemoteModule module in candidate.Values)
            {
                if (!module.Alive) continue;
                if (!ready) { module.PublicMasked = true; module.Host.SetActive(false); }
                else if (module.PublicMasked)
                { module.PublicMasked = false; module.Host.SetActive(!module.StockMasked
                    && EffectiveRemoteFrame(module)?.Visible == true); }
            }
            if (ready) _displayedPublicPeer = -elected;
        }
        if (ready) ClearRetainedPublic();
        else if (RetainedPublic != null && (!Sessions.TryGetValue(-elected, out var session)
            || !session.Active || Time.unscaledTime - session.LastSeenTime > 10f)) ClearRetainedPublic();
    }

    private static void ClearRetainedPublic()
    {
        if (RetainedPublic == null) return;
        foreach (RemoteModule module in RetainedPublic.Values) module.Dispose();
        RetainedPublic = null; _retainedPublicPeer = 0;
    }

    private static void ForgetPublicPicture(int peer)
    {
        if (_retainedPublicPeer == peer) ClearRetainedPublic();
        if (_displayedPublicPeer == peer) _displayedPublicPeer = 0;
        if (_pendingPublicPeer == peer) { _pendingPublicPeer = 0; _pendingPublicTurn = 0; }
    }

    private static void ResetPublicPicture()
    {
        ClearRetainedPublic(); _displayedPublicPeer = _committedPublicAuthor = 0;
        _committedPublicReady = false; _pendingPublicPeer = 0; _pendingPublicTurn = 0;
    }
}
