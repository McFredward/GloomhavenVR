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
                internal bool Public, IsStock;
                internal Transform? _sharedFrame
                { get => GloomhavenVR.WorldUI.TownServiceSync.FixtureFrame669(Public, IsStock);
                  set => GloomhavenVR.WorldUI.TownServiceSync.FixtureFrame669(Public, IsStock, value); }
                internal void ResetCore()
                { if (Public) GloomhavenVR.WorldUI.TownServiceSync.ResetPublic(); else GloomhavenVR.WorldUI.TownServiceSync.Reset(); }
                internal void ReportReset() => GloomhavenVR.WorldUI.TownServiceSync.FixtureDiagnosticReset669(Public, IsStock);
            }
            private static readonly PublisherPort Private = new(), Public = new() { Public = true }, Stock = new() { IsStock = true };
            private static void ResetPublic() => Public.ResetCore();
            private static void Reset() => Private.ResetCore();
        }
        internal static void OrdinaryShutdown() => TownServiceSync.Shutdown();
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
    private static void NativeBankDiagnostic669()
    {
        NativeInitializationProbe.OrdinaryShutdown();
        var hud = Go("Native failed preparation diagnostic HUD").AddComponent<NativeInitializationProbe.UIGuildmasterHUD>();
        hud.Icon = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Diagnostic native icon" }; Assets.Add(hud.Icon);
        NativeInitializationProbe.Select(hud); NativeInitializationProbe.Fail = true;
        int Messages(string prefix) => GloomhavenVR.Core.VRLog.Messages.FindAll(message => message.StartsWith(prefix, StringComparison.Ordinal)).Count;
        const string publisherPrefix = "TownServices: Original widget publisher unavailable:";
        const string mirrorPrefix = "TownServices: Original service presentation unavailable (";
        const string capPrefix = "TownServices: Further presentation errors are suppressed";
        void Report(int identity)
        {
            var failure = new InvalidDataException("Preparation diagnostic " + identity);
            TownServiceSync.FixtureDiagnostic669("prepare", failure);
            typeof(TownServiceMirror).GetMethod("Report", PrivateStatic)!.Invoke(null, new object[] { "prepare", failure });
        }
        void FailPreparation()
        {
            try { NativeInitializationProbe.Initialize(); }
            catch (InvalidDataException error) { Check(error.Message == "Controlled native factory failure", "diagnostic proof preserves the actual native preparation failure"); }
        }
        int publisherStart = Messages(publisherPrefix), mirrorStart = Messages(mirrorPrefix), capStart = Messages(capPrefix);
        FailPreparation(); Report(0);
        for (int retry = 0; retry < 12; retry++) { FailPreparation(); Report(0); }
        Check(Messages(publisherPrefix) == publisherStart + 3,
            "repeated preparation failures retain the original publisher duplicate suppression");
        Check(Messages(mirrorPrefix) == mirrorStart + 1,
            "repeated preparation failures retain the original mirror duplicate suppression");
        for (int distinct = 1; distinct <= 12; distinct++) { FailPreparation(); Report(distinct); }
        Check(Messages(publisherPrefix) == publisherStart + 24 && Messages(mirrorPrefix) == mirrorStart + 8
            && Messages(capPrefix) == capStart + 4,
            "preparation retries preserve the eight-error cap and one suppression marker per diagnostic stream");
        NativeInitializationProbe.OrdinaryShutdown(); Report(0);
        Check(Messages(publisherPrefix) == publisherStart + 27,
            "ordinary map teardown starts a fresh publisher diagnostic lifetime");
        Check(Messages(mirrorPrefix) == mirrorStart + 9,
            "ordinary map teardown starts a fresh mirror diagnostic lifetime");
        NativeInitializationProbe.Fail = false; NativeInitializationProbe.Select(null); NativeInitializationProbe.OrdinaryShutdown();
        TownServiceSync.FixtureDiagnostic669("prepare", new MissingReferenceException(string.Empty));
        Check(GloomhavenVR.Core.VRLog.Messages.Exists(message => message == "TownServices: Original widget publisher unavailable: prepare: MissingReferenceException"),
            "empty native exception message retains its actionable publisher exception type");
        NativeInitializationProbe.OrdinaryShutdown();
    }

    private static void RetiredPublisherContinuation669()
    {
        var fields = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
        var frame = Go("Native replacement continuation frame").transform;
        var first = Go("Native continuation first HUD").AddComponent<NativeInitializationProbe.UIGuildmasterHUD>();
        var replacement = Go("Native continuation replacement HUD").AddComponent<NativeInitializationProbe.UIGuildmasterHUD>();
        first.Icon = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "First native mode icon" }; Assets.Add(first.Icon);
        replacement.Icon = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Replacement native mode icon" }; Assets.Add(replacement.Icon);
        foreach (string laneName in new[] { "private", "public", "stock" })
        {
            NativeInitializationProbe.OrdinaryShutdown(); NativeInitializationProbe.Select(first);
            Check(NativeInitializationProbe.Initialize(), "native publisher starts with a successfully prepared first HUD");
            NativeTemplates.Ready = true; TownServicePresentation.Active = true;
            TownServicePresentation.Service = 1; TownServicePresentation.Session = 669101;
            TownServiceMerchantHandoff.HasParkedOffer = laneName == "stock";
            object lane = typeof(TownServiceMirror).GetField(laneName == "public" ? "PublicLane" : laneName == "stock" ? "StockLane" : "PrivateLane", PrivateStatic)!.GetValue(null)!;
            bool Active() => (bool)lane.GetType().GetField("Active", fields)!.GetValue(lane)!;
            var catalog = GloomhavenVR.WorldUI.TownServicePublicMerchant.Catalog!;
            void Tick()
            {
                if (laneName == "public") TownServiceSync.TickPublic(frame, frame, catalog, 669101, 0);
                else if (laneName == "stock") TownServiceSync.FixtureTickStock669(frame);
                else TownServiceSync.Tick(frame, frame);
            }
            TownServiceSync.FixturePrepare669 = () =>
            { NativeInitializationProbe.Select(replacement); NativeInitializationProbe.Initialize(); };
            Tick();
            Check(!Active() && TownServiceSync.FixtureFrame669(laneName == "public", laneName == "stock") == null,
                "retired " + laneName + " publisher never continues against replaced native originals");
            catalog = GloomhavenVR.WorldUI.TownServicePublicMerchant.Catalog!;
            TownServiceSync.FixturePrepare669 = () => NativeInitializationProbe.Initialize();
            Tick();
            Check(Active() && TownServiceSync.FixtureFrame669(laneName == "public", laneName == "stock") == frame,
                "unchanged " + laneName + " native preparation continues on the next regular publisher tick");
            TownServiceSync.FixturePrepare669 = null;
        }
        TownServiceMerchantHandoff.HasParkedOffer = false; TownServicePresentation.Active = false;
        NativeInitializationProbe.Select(null); NativeInitializationProbe.OrdinaryShutdown();
    }

    private static void TownLaneShutdown669()
    {
        TownServiceSync.ResetNetwork(); TownServiceMirror.Shutdown();
        var frame = Go("Nested-lane shutdown frame").transform;
        var original = Go("Nested-lane native original").transform;
        original.gameObject.AddComponent<CanvasGroup>();
        var live = Go("Nested-lane live original").transform;
        live.gameObject.AddComponent<CanvasGroup>();
        var fields = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        object Lane(string key) => typeof(TownServiceMirror).GetField(key, PrivateStatic)!.GetValue(null)!;
        object Current() => typeof(TownServiceMirror).GetField("_local", PrivateStatic)!.GetValue(null)!;
        bool Active(object lane) => (bool)lane.GetType().GetField("Active", fields | System.Reflection.BindingFlags.Public)!.GetValue(lane)!;
        uint Session(object lane) => (uint)lane.GetType().GetField("Session", fields | System.Reflection.BindingFlags.Public)!.GetValue(lane)!;
        int Modules(object lane) => ((System.Collections.IDictionary)lane.GetType().GetField("Modules", fields | System.Reflection.BindingFlags.Public)!.GetValue(lane)!).Count;
        void Begin(bool isPublic, bool stock, uint session)
        {
            using (stock ? TownServiceMirror.UseStockLane() : isPublic ? TownServiceMirror.UsePublicLane() : TownServiceMirror.UsePrivateLane())
            {
                TownServiceMirror.BeginSession(3, session, frame, frame);
                TownServiceMirror.RegisterTemplate(3, 991, original, _ => false, "enchant.inventory|");
                TownServiceMirror.RegisterModule(991, 991, live, _ => false, "enchant.inventory|");
            }
            object publisher = typeof(TownServiceSync).GetField(stock ? "Stock" : isPublic ? "Public" : "Private", PrivateStatic)!.GetValue(null)!;
            typeof(TownServiceSync).GetField("_session", fields)!.SetValue(publisher, session);
            typeof(TownServiceSync).GetField("_service", fields)!.SetValue(publisher, (byte)3);
        }
        object privateLane = Lane("PrivateLane"), publicLane = Lane("PublicLane"), stockLane = Lane("StockLane");
        Begin(false, false, 669001); Begin(true, false, 669002);
        TownServiceMirror.SetLocalTransactionActive(3, true);
        Check(GloomhavenVR.Net.TownServices.TownServiceGrantSync.Offers.Count == 1,
            "private transaction has a genuinely requested offer before nested teardown");
        using (TownServiceMirror.UsePublicLane())
        {
            TownServiceSync.Reset();
            Check(!Active(privateLane) && Modules(privateLane) == 0 && GloomhavenVR.Net.TownServices.TownServiceGrantSync.Offers.Count == 0
                && Active(publicLane) && Modules(publicLane) == 1 && ReferenceEquals(Current(), publicLane),
                "private publisher reset from the public scope retires only the private session and offer");
        }
        Begin(false, false, 669003); Begin(false, true, 669004); TownServiceMirror.SetLocalTransactionActive(3, true);
        uint assets = TownServiceMirror.Assets.Generation;
        using (TownServiceMirror.UsePublicLane())
        {
            TownServiceSync.ResetNetwork();
            Check(!Active(privateLane) && !Active(publicLane) && !Active(stockLane)
                && Modules(privateLane) == 0 && Modules(publicLane) == 0 && Modules(stockLane) == 0
                && GloomhavenVR.Net.TownServices.TownServiceGrantSync.Offers.Count == 0
                && TownServiceMirror.Assets.Generation == assets && ReferenceEquals(Current(), publicLane),
                "network reset inside the public scope closes every publisher and private offer while retaining original assets");
        }
        Begin(false, false, 669005); Begin(true, false, 669006); Begin(false, true, 669007);
        TownServiceMirror.SetLocalTransactionActive(3, true);
        using (TownServiceMirror.UsePublicLane())
        {
            TownServiceMirror.Shutdown();
            Check(!Active(privateLane) && !Active(publicLane) && !Active(stockLane)
                && Session(privateLane) == 0 && Session(publicLane) == 0 && Session(stockLane) == 0
                && Modules(privateLane) == 0 && Modules(publicLane) == 0 && Modules(stockLane) == 0
                && GloomhavenVR.Net.TownServices.TownServiceGrantSync.Offers.Count == 0 && ReferenceEquals(Current(), publicLane),
                "mirror shutdown inside the public scope retires every private public and stock module");
        }
        TownServiceSync.ResetNetwork(); TownServiceMirror.Shutdown();
    }

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
