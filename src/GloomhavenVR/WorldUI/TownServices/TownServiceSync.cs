using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Publishes owner-authored original native service widgets after their final VR layout.
/// Gameplay controllers remain exclusively on their original local objects.</summary>
internal static class TownServiceSync
{
    private sealed class Published
    {
        internal ushort Id;
        internal string Address = string.Empty;
        internal Transform Source = null!;
        internal Func<Transform, bool> Exclude = null!;
        internal bool Seen;
    }
    private static readonly Dictionary<string, Published> Modules = new(StringComparer.Ordinal);
    private static readonly List<string> Removed = new();
    private static readonly HashSet<Transform> Visited = new();
    private static readonly List<Transform> Dynamic = new();
    private static readonly Dictionary<string, float> Failures = new(StringComparer.Ordinal);
    private static uint _session;
    private static byte _service;
    private static ushort _nextId;
    private static Transform? _sharedFrame;
    internal static void Prepare()
    {
        if (!MapRoomDriver.Active) return;
        try { NativeTemplates.Initialize(); }
        catch (Exception e) { Report("prepare", e); }
    }
    internal static void Tick(Transform sharedFrame, Transform? stationRoot)
    {
        _sharedFrame = sharedFrame;
        TownServiceMirror.SharedFrameForRemote = ResolveFrame;
        Prepare();
        if (!TownServicePresentation.Active || stationRoot == null)
        { Reset(); return; }
        if (!NativeTemplates.Ready) return;
        byte service = TownServicePresentation.Service;
        uint session = TownServicePresentation.Session;
        if (_session != session || _service != service)
        {
            Reset(); _session = session; _service = service; _nextId = 0;
            TownServiceMirror.BeginSession(service, session, sharedFrame, stationRoot);
        }
        foreach (Published module in Modules.Values) module.Seen = false;
        Visited.Clear(); Dynamic.Clear();
        string prefix = service == 1 ? "merchant" : service == 2 ? "temple" : "enchant";
        Publish(prefix, TownServicePresentation.Window != null ? TownServicePresentation.Window.transform : null);
        foreach (TownServiceSurface surface in TownServicePresentation.LocalSurfaces)
            Publish(surface.Id == 10 ? prefix + ".inventory" : surface.Id == 11 ? "enchant.holder" : "enchant.scroll", surface.Panel.Target);
        if (service == 3) Publish("enchant.cards", NativeTemplates.Original("enchant.cards"));
        Publish("banner", NativeTemplates.Original("banner"));
        Publish(prefix + ".tooltip", NativeTemplates.Original(prefix + ".tooltip"));
        Publish("item.confirm", NativeTemplates.Original("item.confirm"));
        Publish("enhance.confirm", NativeTemplates.Original("enhance.confirm"));
        // Original pooled branches are separate modules: adding/removing a row must never change
        // the native static template or replace the inventory container behind another visitor.
        for (int i = 0; i < Dynamic.Count; i++)
        {
            Transform source = Dynamic[i]; if (source == null) continue;
            string? key = DynamicKey(source);
            if (key != null) Publish(key, source);
        }
        foreach (TownServiceToken sample in TownServicePresentation.Samples)
        {
            Transform? held = sample.HeldContent;
            if (held == null) continue;
            string? key = DynamicKey(sample.Source);
            if (key != null) Publish(key, held);
        }
        Removed.Clear();
        foreach (var pair in Modules) if (!pair.Value.Seen) Removed.Add(pair.Key);
        foreach (string key in Removed) { TownServiceMirror.UnregisterModule(Modules[key].Id); Modules.Remove(key); }
    }
    private static Transform? ResolveFrame(int _) => _sharedFrame;
    private static string? DynamicKey(Transform source)
    {
        if (source.GetComponent<UIShopItemSlot>() != null) return "merchant.row";
        if (source.GetComponent<UITempleShopSlot>() != null) return "temple.row";
        if (source.GetComponent<UINewEnhancementShopSlot>() != null) return "enchant.row";
        if (source.GetComponent<UIEnhanceCardSlot>() != null) return "enchant.cardrow";
        if (source.GetComponent<UIEnhanceCardPoint>() != null) return "enchant.point";
        if (source.GetComponent<UIEnhancementButtonHighlight>() != null) return "enchant.highlight";
        AbilityCardUI? card = source.GetComponent<AbilityCardUI>(); if (card != null) return NativeTemplates.CardKey(card);
        ItemCardUI? item = source.GetComponent<ItemCardUI>(); if (item != null) return "item." + item.CardID;
        return null;
    }
    private static void Publish(string key, Transform? source)
    {
        if (source == null || !Visited.Add(source)) return;
        try
        {
            // Include inactive static widgets, since their hierarchy/parent clipping is needed by
            // active children during native transitions. A whole inactive pooled row needs no lane.
            if (NativeTemplates.IsDynamic(source) && !source.gameObject.activeInHierarchy) return;
            CollectDynamic(source);
            IReadOnlyList<NativeTemplates.Part> parts = NativeTemplates.Parts(key);
            foreach (NativeTemplates.Part part in parts)
            {
                Transform? root = NativeTemplates.At(source, part.Path);
                if (root == null) throw new InvalidDataException("Original subtree is absent: " + key + "|" + part.Path);
                string address = key + "|" + part.Path, identity = address + "@" + source.GetInstanceID();
                if (!Modules.TryGetValue(identity, out Published? module))
                {
                    if (Modules.Count >= TownServiceFrame.MaxModules || _nextId >= ushort.MaxValue - 1)
                        throw new InvalidDataException("Town service exceeds the simultaneous module budget.");
                    var excluded = new HashSet<Transform>();
                    foreach (NativeTemplates.Part other in parts)
                    {
                        if (other == part) continue;
                        Transform? child = NativeTemplates.At(source, other.Path);
                        if (child != null && child.IsChildOf(root)) excluded.Add(child);
                    }
                    module = new Published { Id = ++_nextId, Address = address, Source = root,
                        Exclude = child => excluded.Contains(child) || NativeTemplates.IsBoundary(child) };
                    // The template address is independent of a peer's pool order and module IDs.
                    NativeTemplates.Resolve(_service, 1, address);
                    TownServiceMirror.RegisterModule(module.Id, 1, root, module.Exclude, address);
                    Modules.Add(identity, module);
                }
                module.Seen = true;
            }
        }
        catch (Exception e) { Report(key, e); }
    }
    private static void CollectDynamic(Transform root)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (NativeTemplates.IsDynamic(child)) { Dynamic.Add(child); continue; }
            if (NativeTemplates.IsBoundary(child)) continue;
            CollectDynamic(child);
        }
    }
    internal static void Reset()
    {
        if (_session != 0) TownServiceMirror.EndSession();
        Modules.Clear(); Visited.Clear(); Dynamic.Clear(); Removed.Clear(); _session = 0; _service = 0;
    }
    internal static void ResetNetwork() { Reset(); TownServiceMirror.ResetNetwork(); }
    internal static void Shutdown()
    { Reset(); TownServiceMirror.Shutdown(); NativeTemplates.Shutdown(); _sharedFrame = null; Failures.Clear(); }
    private static void Report(string scope, Exception e)
    {
        float now = Time.unscaledTime;
        string key = scope + ": " + e.Message;
        if (Failures.TryGetValue(key, out float then) && now - then < 30) return;
        if (Failures.Count > 32) Failures.Clear(); Failures[key] = now;
        VRLog.Note("TownServices", "Original widget publisher unavailable: " + key);
    }
}
