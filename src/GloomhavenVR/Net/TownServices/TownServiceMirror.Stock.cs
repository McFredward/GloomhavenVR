using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    // Public mirrors use -peer and private visitors use +peer. Cosmetic held stock
    // uses explicitly allocated reserved keys, never arithmetic that can overflow
    // for a valid Photon peer. A future public collision is refused, not aliased.
    private static readonly Dictionary<int, int> StockKeys = new();
    private static readonly Dictionary<int, int> StockPeers = new();
    private static readonly HashSet<int> StockHeldIds = new();
    private static readonly HashSet<ushort> StockMountIds = new();
    private static readonly HashSet<ushort> DuplicatePublicMounts = new();

    private static bool TryStockPeerKey(int peer, out int key)
    {
        if (StockKeys.TryGetValue(peer, out key)) return true;
        for (int slot = 0; slot < 24; slot++)
        {
            int candidate = int.MinValue + slot;
            if (candidate == -peer || StockPeers.ContainsKey(candidate)
                || Sessions.ContainsKey(candidate) || Pending.ContainsKey(candidate)) continue;
            StockKeys.Add(peer, candidate); StockPeers.Add(candidate, peer); key = candidate; return true;
        }
        Report("stock peer namespace", new InvalidDataException("Held stock peer namespace is full."));
        key = 0; return false;
    }

    private static bool IsStockPeerKey(int key) => StockPeers.ContainsKey(key);
    private static int RealPeer(int key) => StockPeers.TryGetValue(key, out int peer)
        ? peer : key < 0 ? -key : key;
    private static void RemoveStockPeer(int peer)
    {
        if (!StockKeys.TryGetValue(peer, out int key)) return;
        ClearRemoteModules(key); Pending.Remove(key); ReceivedBaselines.Remove(key); Sessions.Remove(key);
        StockKeys.Remove(peer); StockPeers.Remove(key);
    }
    private static void ClearStockPeerKeys()
    { StockKeys.Clear(); StockPeers.Clear(); StockHeldIds.Clear(); StockMountIds.Clear(); DuplicatePublicMounts.Clear(); }

    private static bool StockModule(string address) => address == "merchant.heldstock|"
        || address.StartsWith("inspectionbody.", StringComparison.Ordinal)
        || address.StartsWith("map.cardbody|", StringComparison.Ordinal)
        || CosmeticAbilityFace(address)
        || address.StartsWith("merchant.heldstock.body|", StringComparison.Ordinal)
        || address.StartsWith("merchant.heldstock.row|", StringComparison.Ordinal)
        || TryStockItemId(address, out _);
    private static bool CosmeticAbilityFace(string address)
    {
        if (!address.StartsWith("face.", StringComparison.Ordinal)) return false;
        int end = address.IndexOf('|');
        return end > 5 && int.TryParse(address.Substring(5, end - 5), NumberStyles.None,
            CultureInfo.InvariantCulture, out int id) && id > 0;
    }
    private static bool TryStockItemId(string address, out int id)
    {
        id = 0;
        if (!address.StartsWith("item.", StringComparison.Ordinal)) return false;
        int end = address.IndexOf('|');
        return end > 5 && int.TryParse(address.Substring(5, end - 5), NumberStyles.None,
            CultureInfo.InvariantCulture, out id) && id > 0;
    }

    /// <summary>Only a live, explicitly typed original stock mount owns a shelf sample.
    /// An owned fan item with the same model ID never vacates the public shelf.</summary>
    internal static bool StockItemHeldByOther(int itemId)
    {
        if (itemId <= 0) return false;
        if (NetAvatarDriver.IsTownStockHeld(itemId)) return true;
        foreach (var pair in StockPeers)
            if (pair.Value != LocalPeer && LiveStockItems(pair.Key, itemId)) return true;
        return false;
    }

    private static bool LiveStockItems(int key, int wanted = 0)
    {
        if (!Sessions.TryGetValue(key, out TownServiceSessionInfo? session) || !session.Active
            || session.Service != 1 || Time.unscaledTime - session.LastSeenTime > NetProtocol.StaleTimeoutSeconds) return false;
        bool found = NetAvatarDriver.TryGetTownHeldStock(RealPeer(key), wanted, out int avatarItem);
        if (found && wanted == 0) StockHeldIds.Add(avatarItem);
        if (!Pending.TryGetValue(key, out Dictionary<ushort, TownServiceFrame>? frames)) return found;
        StockMountIds.Clear();
        foreach (TownServiceFrame pending in frames.Values)
        {
            TownServiceFrame frame = EffectiveStockFrame(key, pending);
            if (frame.Session == session.Session && frame.Visible && frame.TemplateAddress == "merchant.heldstock|"
                && Array.BinarySearch(session.Modules, frame.Module) >= 0) StockMountIds.Add(frame.Module);
        }
        foreach (TownServiceFrame pending in frames.Values)
        {
            TownServiceFrame frame = EffectiveStockFrame(key, pending);
            if (frame.Session != session.Session || !frame.Visible || Array.BinarySearch(session.Modules, frame.Module) < 0
                || !TryStockItemId(frame.TemplateAddress, out int id)) continue;
            // Parts of a large original card may have another item part as parent.
            // Resolve only this live owner's original mount chain, never a viewer fit.
            ushort parent = frame.ParentModule;
            for (int steps = 0; steps < TownServiceFrame.MaxModules; steps++)
            {
                if (StockMountIds.Contains(parent))
                { if (wanted == 0) { StockHeldIds.Add(id); found = true; } else if (id == wanted) found = true; break; }
                if (!frames.TryGetValue(parent, out TownServiceFrame? ancestor)) break;
                ancestor = EffectiveStockFrame(key, ancestor);
                if (ancestor.Session != session.Session || !ancestor.Visible
                    || Array.BinarySearch(session.Modules, ancestor.Module) < 0
                    || ancestor.ParentModule == parent) break;
                parent = ancestor.ParentModule;
            }
        }
        return found;
    }

    private static TownServiceFrame EffectiveStockFrame(int key, TownServiceFrame pending)
    {
        if (Remote.TryGetValue(key, out var modules) && modules.TryGetValue(pending.Module, out var module)
            && module.Alive && EffectiveRemoteFrame(module) is TownServiceFrame applied
            && applied.Session == pending.Session && applied.Structure == pending.Structure
            && applied.TemplateAddress == pending.TemplateAddress && applied.SampleTime >= pending.SampleTime)
            return applied;
        return pending;
    }

    // A pickup cue belongs to an actual visible original lifted stock sample,
    // independently of the visitor's current private NPC focus and transaction.
    private static bool TryStockVoiceSession(int realPeer, TownServiceFrame frame,
        out TownServiceSessionInfo? session)
    {
        session = null;
        return frame.VisitorStock && frame.Service == 1
            && StockKeys.TryGetValue(realPeer, out int key)
            && Sessions.TryGetValue(key, out session) && session.Session == frame.Session
            && LiveStockItems(key);
    }

    private static void SetSecondaryTempleInscriptions(TownServiceSessionInfo session,
        Dictionary<ushort, RemoteModule> modules)
    {
        bool visiblePurse = false;
        foreach (RemoteModule body in modules.Values)
        {
            TownServiceFrame? frame = EffectiveRemoteFrame(body);
            if (frame == null || !body.Alive) continue;
            if (frame.Session == session.Session && (frame.TemplateAddress == "ritual.purse.held|"
                    || frame.TemplateAddress == "ritual.purse|" && PhysicalPurse(frame.Nodes))
                && frame.Visible && frame.ParentAlpha > .01f
                && Array.BinarySearch(session.Modules, frame.Module) >= 0) { visiblePurse = true; break; }
        }
        foreach (RemoteModule module in modules.Values)
        {
            if (!module.Alive || !module.Address.StartsWith("temple.row|", StringComparison.Ordinal)
                || module.LastFrame == null) continue;
            // The palm-gated purse preview is a real owner's fan, already public
            // before pickup. Election of another visitor's shared book/bowl must
            // not hide either this original purse or its original inscriptions.
            bool shown = visiblePurse && EffectiveRemoteFrame(module)!.Visible;
            if (module.Host.activeSelf != shown) module.Host.SetActive(shown);
        }
    }

    private static void SuppressPreparedHeldStock()
    {
        foreach (var stock in StockPeers)
        {
            if (!Remote.TryGetValue(stock.Key, out Dictionary<ushort, RemoteModule>? modules)) continue;
            foreach (RemoteModule module in modules.Values)
            {
                if (!module.Alive || module.Address != "merchant.heldstock|" || module.LastFrame == null) continue;
                bool held = false;
                foreach (RemoteModule item in modules.Values)
                {
                    TownServiceFrame? frame = EffectiveRemoteFrame(item);
                    if (frame == null || !TryStockItemId(frame.TemplateAddress, out int id)
                        || !NetAvatarDriver.TryGetTownHeldStock(stock.Value, id, out _)) continue;
                    ushort parent = frame.ParentModule;
                    for (int steps = 0; steps < modules.Count; steps++)
                    {
                        if (parent == module.LastFrame.Module) { held = true; break; }
                        if (!modules.TryGetValue(parent, out RemoteModule? ancestor)
                            || EffectiveRemoteFrame(ancestor) is not TownServiceFrame ancestorFrame
                            || ancestorFrame.ParentModule == parent) break;
                        parent = ancestorFrame.ParentModule;
                    }
                    if (held) break;
                }
                // Exact original metadata is prepared during the canonical avatar hold, but
                // there is one visible card. Release exposes its already-warm return module
                // immediately; it cannot await a different item's avatar or local distance.
                module.StockMasked = held;
                TownServiceFrame current = EffectiveRemoteFrame(module)!;
                bool shown = !held && current.Visible;
                if (module.Host.activeSelf != shown) module.Host.SetActive(shown);
            }
        }
    }

    private static void SuppressRemoteStockDuplicates()
    {
        SuppressPreparedHeldStock();
        StockHeldIds.Clear();
        if (StockLane.Active)
            foreach (LocalModule item in StockLane.Modules.Values)
            {
                if (!TryStockItemId(item.Address, out int id) || item.Binding.Root == null
                    || !item.Binding.Root.gameObject.activeInHierarchy) continue;
                foreach (LocalModule mount in StockLane.Modules.Values)
                    if (mount.Address == "merchant.heldstock|" && mount.Binding.Root != null
                        && item.Binding.Root.IsChildOf(mount.Binding.Root)) { StockHeldIds.Add(id); break; }
            }
        foreach (int key in StockPeers.Keys) LiveStockItems(key);
        int author = PublicAuthor;
        if (author == int.MaxValue || !Remote.TryGetValue(-author, out Dictionary<ushort, RemoteModule>? modules)) return;
        DuplicatePublicMounts.Clear();
        foreach (RemoteModule item in modules.Values)
        {
            TownServiceFrame? frame = EffectiveRemoteFrame(item);
            if (frame == null || !TryStockItemId(item.Address, out int id)
                || !StockHeldIds.Contains(id) && !NetAvatarDriver.IsTownStockHeld(id)
                    && !WorldUI.TownServiceCatalog.HasLocallyHeldStock(id)) continue;
            ushort parent = frame.ParentModule;
            for (int steps = 0; steps < modules.Count; steps++)
            {
                if (!modules.TryGetValue(parent, out RemoteModule? ancestor)
                    || EffectiveRemoteFrame(ancestor) is not TownServiceFrame ancestorFrame) break;
                if (ancestor.Address == "merchant.cardmount|") { DuplicatePublicMounts.Add(parent); break; }
                if (ancestorFrame.ParentModule == parent) break;
                parent = ancestorFrame.ParentModule;
            }
        }
        foreach (var pair in modules)
        {
            RemoteModule module = pair.Value;
            if (!module.Alive || module.Address != "merchant.cardmount|" || module.LastFrame == null) continue;
            if (DuplicatePublicMounts.Contains(pair.Key))
            {
                module.StockMasked = true;
                if (module.Host.activeSelf) module.Host.SetActive(false);
            }
            else if (module.StockMasked)
            {
                // The rack already computed page/dependency visibility this tick.
                // Restore only our own mask; a warm off-page native slot must stay
                // hidden rather than acquiring a second, invented page author.
                module.StockMasked = false;
                TownServiceFrame current = EffectiveRemoteFrame(module)!;
                bool shown = current.Visible;
                TownRackStamp? stamp = current.RackMember;
                if (stamp != null && !stamp.Detached)
                    shown &= module.Host.GetComponent<CanvasGroup>().alpha > .01f;
                if (module.Host.activeSelf != shown) module.Host.SetActive(shown);
            }
        }
    }
}
