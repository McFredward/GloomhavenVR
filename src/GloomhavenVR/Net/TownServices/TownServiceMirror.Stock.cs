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
        || address.StartsWith("merchant.heldstock.body|", StringComparison.Ordinal)
        || address.StartsWith("merchant.heldstock.row|", StringComparison.Ordinal)
        || TryStockItemId(address, out _);
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
        foreach (var pair in StockPeers)
            if (pair.Value != LocalPeer && LiveStockItems(pair.Key, itemId)) return true;
        return false;
    }

    private static bool LiveStockItems(int key, int wanted = 0)
    {
        if (!Sessions.TryGetValue(key, out TownServiceSessionInfo? session) || !session.Active
            || session.Service != 1 || Time.unscaledTime - session.LastSeenTime > NetProtocol.StaleTimeoutSeconds
            || !Pending.TryGetValue(key, out Dictionary<ushort, TownServiceFrame>? frames)) return false;
        StockMountIds.Clear();
        foreach (TownServiceFrame frame in frames.Values)
            if (frame.Session == session.Session && frame.Visible && frame.TemplateAddress == "merchant.heldstock|"
                && Array.BinarySearch(session.Modules, frame.Module) >= 0) StockMountIds.Add(frame.Module);
        bool found = false;
        foreach (TownServiceFrame frame in frames.Values)
        {
            if (frame.Session != session.Session || !frame.Visible || Array.BinarySearch(session.Modules, frame.Module) < 0
                || !TryStockItemId(frame.TemplateAddress, out int id)) continue;
            // Parts of a large original card may have another item part as parent.
            // Resolve only this live owner's original mount chain, never a viewer fit.
            ushort parent = frame.ParentModule;
            for (int steps = 0; steps < TownServiceFrame.MaxModules; steps++)
            {
                if (StockMountIds.Contains(parent))
                { if (wanted == 0) StockHeldIds.Add(id); else if (id == wanted) found = true; break; }
                if (!frames.TryGetValue(parent, out TownServiceFrame? ancestor)
                    || ancestor.Session != session.Session || !ancestor.Visible
                    || Array.BinarySearch(session.Modules, ancestor.Module) < 0
                    || ancestor.ParentModule == parent) break;
                parent = ancestor.ParentModule;
            }
        }
        return found;
    }

    private static void SetSecondaryTempleInscriptions(TownServiceSessionInfo session,
        Dictionary<ushort, TownServiceFrame> pending, Dictionary<ushort, RemoteModule> modules)
    {
        bool heldPurse = false;
        foreach (TownServiceFrame frame in pending.Values)
            if (frame.Session == session.Session && frame.TemplateAddress == "ritual.purse.held|"
                && frame.Visible && frame.ParentAlpha > .01f
                && Array.BinarySearch(session.Modules, frame.Module) >= 0) { heldPurse = true; break; }
        foreach (RemoteModule module in modules.Values)
        {
            if (!module.Alive || !module.Address.StartsWith("temple.row|", StringComparison.Ordinal)
                || module.LastFrame == null) continue;
            bool shown = heldPurse && module.LastFrame.Visible;
            if (module.Host.activeSelf != shown) module.Host.SetActive(shown);
        }
    }

    private static void SuppressRemoteStockDuplicates()
    {
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
            TownServiceFrame? frame = item.LastFrame;
            if (frame == null || !TryStockItemId(item.Address, out int id) || !StockHeldIds.Contains(id)) continue;
            ushort parent = frame.ParentModule;
            for (int steps = 0; steps < modules.Count; steps++)
            {
                if (!modules.TryGetValue(parent, out RemoteModule? ancestor) || ancestor.LastFrame == null) break;
                if (ancestor.Address == "merchant.cardmount|") { DuplicatePublicMounts.Add(parent); break; }
                if (ancestor.LastFrame.ParentModule == parent) break;
                parent = ancestor.LastFrame.ParentModule;
            }
        }
        foreach (var pair in modules)
        {
            RemoteModule module = pair.Value;
            if (!module.Alive || module.Address != "merchant.cardmount|" || module.LastFrame == null) continue;
            bool shown = module.LastFrame.Visible && !DuplicatePublicMounts.Contains(pair.Key);
            if (module.Host.activeSelf != shown) module.Host.SetActive(shown);
        }
    }
}
