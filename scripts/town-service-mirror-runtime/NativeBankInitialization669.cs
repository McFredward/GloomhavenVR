using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI
{
    // Native HUD availability, unrelated factory work and enhancement warm-up are ports.
    // Initialize, Ready and Shutdown compile directly from production; actual Unity bank
    // objects and the complete strict asset/mirror registry retain their real lifetimes.
    internal static partial class NativeInitializationProbe
    {
        internal sealed class UIGuildmasterHUD : MonoBehaviour { internal Texture2D Icon = null!; }
        private static class Singleton<T> { internal static T? Instance; }
        private static class TownServiceCardBody { internal static void Dispose(GameObject body) { Object.Destroy(body); } }
        private sealed class TownServiceTray { internal void Dispose() { } }
        private static class TownServiceMerchantHandoff { internal static void Reset() { } }
        private static class TownServiceNativeAssets { internal static void Shutdown() { } }
        private static class TownServicePublicMerchant
        {
            internal static int Resets;
            internal static void Reset()
            { Resets++; GloomhavenVR.WorldUI.TownServicePublicMerchant.Catalog = null; }
        }
        private static partial class TownServiceSync
        {
            private sealed class PublisherPort
            {
                internal bool Public;
                internal Transform? _sharedFrame;
                internal void ResetCore()
                { if (Public) GloomhavenVR.WorldUI.TownServiceSync.ResetPublic(); else GloomhavenVR.WorldUI.TownServiceSync.Reset(); }
                internal void ReportReset() { }
            }
            private static readonly PublisherPort Private = new(), Public = new() { Public = true };
            private static void ResetPublic() => Public.ResetCore();
        }
        internal static int PublicCatalogResets => TownServicePublicMerchant.Resets;
        private static readonly Dictionary<string, object> Entries = new();
        private static readonly Dictionary<Transform, string> Roots = new();
        private static readonly List<GameObject> PhysicalTemplates = new();
        private static GameObject? _bank, _catalogNavigation, _cardBody, _mapBacking;
        private static UIGuildmasterHUD? _hud;
        private static bool _ready;
        private static uint _assetGeneration;
        private static TownServiceTray? _tray;
        private static object? Tooltip;
        internal const string IconSlot = "native-town|template|banner/SWNvbg==:0|image|texture";
        internal static bool Fail, FailAfterReady;
        internal static int Factories, Preparations;
        internal static GameObject? Bank => _bank;
        internal static bool Clean => _bank == null && _hud == null && !_ready && Entries.Count == 0 && Roots.Count == 0;
        internal static void Select(UIGuildmasterHUD? hud) => Singleton<UIGuildmasterHUD>.Instance = hud;
        private static bool InitializeBank(UIGuildmasterHUD hud)
        {
            Factories++; _hud = hud;
            _bank = new GameObject("Actual inactive original bank"); _bank.SetActive(false);
            Object.DontDestroyOnLoad(_bank);
            TownServiceMirror.Assets.RegisterOriginal(IconSlot, hud.Icon);
            Entries.Add("banner", _bank.transform); Roots.Add(_bank.transform, "banner");
            _assetGeneration = TownServiceMirror.Assets.Generation;
            if (FailAfterReady) _ready = true;
            if (Fail) throw new InvalidDataException("Controlled native factory failure");
            _ready = true;
            GloomhavenVR.WorldUI.TownServicePublicMerchant.Catalog = new TownServiceCatalog();
            GloomhavenVR.WorldUI.TownServicePublicMerchant.StationRoot = hud.transform;
            PrepareEnhancementOriginals(); return true;
        }
        private static void BindOriginalBackdrops()
        {
            TownServiceMirror.Assets.RegisterOriginal(IconSlot, _hud!.Icon);
            _assetGeneration = TownServiceMirror.Assets.Generation;
        }
        private static void PrepareEnhancementOriginals() => Preparations++;
        private static void ResetEnhancementPreparation() { }
    }
}

public static partial class MirrorProgram
{
    private static void NativeBankInitialization669()
    {
        TownServiceMirror.Shutdown(); NativeInitializationProbe.Shutdown();
        NativeInitializationProbe.Select(null);
        uint generation = TownServiceMirror.Assets.Generation;
        Check(!NativeInitializationProbe.Initialize() && NativeInitializationProbe.Clean
            && TownServiceMirror.Assets.Generation == generation,
            "missing native HUD does not start or reset an original bank");
        var first = Go("Native first HUD").AddComponent<NativeInitializationProbe.UIGuildmasterHUD>();
        var replacement = Go("Native replacement HUD").AddComponent<NativeInitializationProbe.UIGuildmasterHUD>();
        Texture2D Icon(Color color)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Native mode icon" };
            texture.SetPixels(new[] { color, color, color, color }); texture.Apply(); Assets.Add(texture); return texture;
        }
        first.Icon = Icon(Color.red); replacement.Icon = Icon(Color.blue);
        NativeInitializationProbe.Select(first);
        NativeInitializationProbe.Fail = true;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            first.Icon = Icon(attempt == 0 ? Color.red : Color.green);
            NativeInitializationProbe.FailAfterReady = attempt == 2;
            bool refused = false;
            try { NativeInitializationProbe.Initialize(); }
            catch (InvalidDataException failure) { refused = failure.Message == "Controlled native factory failure"; }
            Check(refused, "failed preparation preserves its native factory error");
            Check(NativeInitializationProbe.Clean,
                "failed preparation leaves no partial bank or ready HUD");
            Check(TownServiceMirror.Assets.Generation != generation,
                "failed preparation retires its immutable registry before native mode art changes");
            generation = TownServiceMirror.Assets.Generation;
        }
        NativeInitializationProbe.Fail = NativeInitializationProbe.FailAfterReady = false;
        first.Icon = Icon(Color.yellow);
        Check(NativeInitializationProbe.Initialize() && NativeInitializationProbe.Ready,
            "a fresh native retry accepts current mode artwork after failed initialization");
        Check(ReferenceEquals(TownServiceMirror.Assets.Resolve<Texture2D>(NativeInitializationProbe.IconSlot), first.Icon),
            "successful retry resolves its exact current native icon");
        GameObject bank = NativeInitializationProbe.Bank!;
        generation = TownServiceMirror.Assets.Generation;
        int factories = NativeInitializationProbe.Factories;
        for (int repeat = 0; repeat < 12; repeat++)
            Check(NativeInitializationProbe.Initialize() && NativeInitializationProbe.Bank == bank
                && NativeInitializationProbe.Factories == factories && TownServiceMirror.Assets.Generation == generation,
                "routine same-HUD preparation preserves its original bank and asset generation");
        TownServiceMirror.Assets.Clear();
        generation = TownServiceMirror.Assets.Generation;
        Check(NativeInitializationProbe.Initialize() && NativeInitializationProbe.Bank == bank
            && NativeInitializationProbe.Factories == factories && TownServiceMirror.Assets.Generation == generation
            && ReferenceEquals(TownServiceMirror.Assets.Resolve<Texture2D>(NativeInitializationProbe.IconSlot), first.Icon),
            "external registry reset rebinds the surviving bank without changing its original copy");
        // An existing template copy must retire with its source bank, even when its
        // numeric address is reused by a replacement native HUD.
        TownServiceMirror.RegisterTemplate(1, 991, bank.transform, _ => false, "banner|");
        var templates = (Dictionary<string, GameObject>)typeof(TownServiceMirror).GetField("Templates", PrivateStatic)!.GetValue(null)!;
        Check(templates.Count > 0, "old native bank has a genuinely registered inert mirror template");
        var publisherSources = new List<System.Collections.IDictionary>();
        foreach (string lane in new[] { "Private", "Public" })
        {
            object publisher = typeof(TownServiceSync).GetField(lane, PrivateStatic)!.GetValue(null)!;
            var sources = (System.Collections.IDictionary)typeof(TownServiceSync).GetField("Sources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(publisher)!;
            Type entryType = typeof(TownServiceSync).GetNestedType("SourceEntry", System.Reflection.BindingFlags.NonPublic)!;
            sources.Add(bank.transform, Activator.CreateInstance(entryType, true)); publisherSources.Add(sources);
        }
        int catalogResets = NativeInitializationProbe.PublicCatalogResets;
        TownServiceCatalog oldCatalog = GloomhavenVR.WorldUI.TownServicePublicMerchant.Catalog!;
        NativeInitializationProbe.Select(replacement);
        bool replaced = false;
        try { replaced = NativeInitializationProbe.Initialize(); } catch (InvalidDataException) { }
        Check(replaced && NativeInitializationProbe.Ready
            && NativeInitializationProbe.Bank != bank && NativeInitializationProbe.Factories == factories + 1
            && TownServiceMirror.Assets.Generation != generation && templates.Count == 0
            && ReferenceEquals(TownServiceMirror.Assets.Resolve<Texture2D>(NativeInitializationProbe.IconSlot), replacement.Icon),
            "a replacement HUD retires all old mirror bindings before registering its own originals");
        Check(publisherSources.TrueForAll(sources => sources.Count == 0),
            "a replacement HUD clears both actual original publisher source dictionaries");
        Check(NativeInitializationProbe.PublicCatalogResets == catalogResets + 1
            && GloomhavenVR.WorldUI.TownServicePublicMerchant.Catalog != null
            && !ReferenceEquals(GloomhavenVR.WorldUI.TownServicePublicMerchant.Catalog, oldCatalog)
            && GloomhavenVR.WorldUI.TownServicePublicMerchant.StationRoot == replacement.transform,
            "a replacement HUD retires its public native catalog before fresh acquisition");
        bool conflicting = false;
        try { TownServiceMirror.Assets.RegisterOriginal(NativeInitializationProbe.IconSlot, first.Icon); }
        catch (InvalidDataException failure) { conflicting = failure.Message.StartsWith("Conflicting original town-service provenance:", StringComparison.Ordinal); }
        Check(conflicting && ReferenceEquals(TownServiceMirror.Assets.Resolve<Texture2D>(NativeInitializationProbe.IconSlot), replacement.Icon),
            "different originals still conflict inside one successfully initialized bank");
        GloomhavenVR.WorldUI.TownServicePublicMerchant.Catalog = null;
        GloomhavenVR.WorldUI.TownServicePublicMerchant.StationRoot = null;
        NativeInitializationProbe.Select(null); NativeInitializationProbe.Shutdown(); TownServiceMirror.Shutdown();
        Check(NativeInitializationProbe.Clean, "explicit bank teardown remains complete after recovery");
    }
}
