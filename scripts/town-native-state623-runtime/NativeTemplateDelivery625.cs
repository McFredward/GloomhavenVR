using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    private static IEnumerator NativeTemplateLifecycle625()
    {
        IEnumerator disconnect = NativeTemplateDisconnect625();
        while (disconnect.MoveNext()) yield return disconnect.Current;
        IEnumerator reset = NativeTemplateResetPreparation625();
        while (reset.MoveNext()) yield return reset.Current;
    }

    private static IDictionary NativeTemplateStore625(string name) =>
        (IDictionary)typeof(TownServiceMirror).GetField(name, PrivateStatic)!.GetValue(null)!;

    private static IEnumerator NativeTemplateDisconnect625()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear();
        Transform author = Go("cold admission source").transform;
        Transform viewer = Go("cold admission observer").transform;
        // The source owns real Unity graphics and the mirror freezes its actual
        // original. Only native dependency readiness is an explicit fixture port.
        Transform source = Source(author);
        const string address = "face.62511|";
        TownServiceMirror.RegisterTemplate(3, 31, source, address: address);
        TownServiceFrame old = NativeFrame623(source, 31, 31, address);
        old.Session = 62511; old.Sequence = 9002;
        Check(TownServiceMirror.TryWriteNativeTemplateState(old, out byte[] oldPacket),
            "disconnect proof uses actual independently reconstructable original metadata");
        var templates = (Dictionary<string, GameObject>)NativeTemplateStore625("Templates");
        string key = (string)typeof(TownServiceMirror).GetMethod("TemplateKey", PrivateStatic)!
            .Invoke(null, new object[] { (byte)3, (ushort)31, address })!;
        GameObject frozen = templates[key]; templates.Remove(key);
        bool ready = false;
        Func<byte, ushort, string, bool>? previous = TownServiceMirror.ResolveTemplate;
        TownServiceMirror.ResolveTemplate = (_, _, wanted) =>
        {
            if (!ready || wanted != address) return false;
            templates[key] = frozen; return true;
        };
        try
        {
            InteractionManifest(2, 3, old.Session, 9001, modules: new ushort[] { 31 });
            Check(TownServiceMirror.Receive(2, oldPacket, oldPacket.Length), "first cold peer frame is retained");
            Check(TownServiceMirror.Receive(3, oldPacket, oldPacket.Length), "second peer's independent cold frame is retained");
            Check(NativeTemplateStore625("UnpreparedNativeTemplates").Count == 2,
                "two actual cold metadata lifetimes are queued independently");
            TownServiceMirror.RemovePeer(2);
            ready = true;
            for (float until = Time.unscaledTime + .11f; Time.unscaledTime < until;)
            { TownServiceMirror.TickRemote(_ => viewer); yield return null; }
            Check(!NativeTemplateStore625("Pending").Contains(2)
                && !NativeTemplateStore625("ReceivedBaselines").Contains(2),
                "departed cold metadata cannot recreate pending or baseline state");
            Check(NativeTemplateStore625("Pending").Contains(3),
                "disconnect retains the other peer's original admission order");

            // A restarted peer may begin its process/session sequence below the
            // departed packet. Neither an old queue slot nor old baseline wins.
            templates.Remove(key); ready = false;
            typeof(TownServiceMirror).GetMethod("ResetNativeTemplateState", PrivateStatic)!.Invoke(null, null);
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Check(TownServiceMirror.Receive(2, oldPacket, oldPacket.Length), "repeat cold connection is retained");
                TownServiceMirror.RemovePeer(2);
            }
            var order = typeof(TownServiceMirror).GetField("UnpreparedNativeOrder", PrivateStatic)!.GetValue(null)!;
            int queued = (int)order.GetType().GetProperty("Count")!.GetValue(order)!;
            Check(NativeTemplateStore625("UnpreparedNativeTemplates").Count == 0 && queued == 0,
                "rapid disconnect before a receiver tick cannot accumulate obsolete queue positions");
            TownServiceFrame current = TownServiceDelta.Retain(old); current.Sequence = 2;
            current.Nodes[0] = new TownServiceNode { Binding = old.Nodes[0].Binding };
            foreach (var property in old.Nodes[0].Values) current.Nodes[0].Values.Add(property.Key, property.Value);
            current.Nodes[0].Values[TownServiceProperty.Transform] = new TownServiceValue
            { Numbers = (float[])old.Nodes[0].Values[TownServiceProperty.Transform].Numbers.Clone() };
            current.Nodes[0].Values[TownServiceProperty.Transform].Numbers[0] += 17f;
            Check(TownServiceMirror.TryWriteNativeTemplateState(current, out byte[] currentPacket) == false,
                "unavailable true source original cannot be encoded by an invented fallback");
            // Restore only to serialize the genuine current owner sample, then
            // hold the receiver's original dependency cold again.
            templates[key] = frozen;
            Check(TownServiceMirror.TryWriteNativeTemplateState(current, out currentPacket),
                "new lower-sequence owner snapshot retains its exact native original");
            templates.Remove(key);
            InteractionManifest(2, 3, current.Session, 1, modules: new ushort[] { 31 });
            Check(TownServiceMirror.Receive(2, currentPacket, currentPacket.Length), "quick reconnect current frame is retained");
            ready = true;
            for (float until = Time.unscaledTime + .25f; Time.unscaledTime < until;)
            { TownServiceMirror.TickRemote(_ => viewer); yield return null; }
            var pending = (IDictionary)NativeTemplateStore625("Pending")[2]!;
            Check(pending[(ushort)31] is TownServiceFrame admitted && admitted.Sequence == 2,
                "quick same-identity reconnect admits the current lower-sequence frame after readiness");
            Check(Remote(2, 31) != null,
                "quick reconnect displays the current original rather than an obsolete deferred offer");
            File.WriteAllText(Path.Combine(_output, "native-template-disconnect625.txt"),
                "Actual Receive -> cold template -> RemovePeer -> Tick; other peer retained; 12 quick cycles; lower-sequence reconnect admitted.\n");
        }
        finally { TownServiceMirror.ResolveTemplate = previous; templates[key] = frozen; }
    }

    private static IEnumerator NativeTemplateResetPreparation625()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear();
        Transform owner = Go("existing frozen bank owner").transform;
        Transform widget = Source(owner);
        NativeTemplates.FixtureInstallFrozenBank625(Go("existing inactive native bank"), widget, 62512);
        uint assets = TownServiceMirror.Assets.Generation;
        try
        {
            for (int frame = 0; frame < 16; frame++)
            { NativeTemplates.FixturePrepareOriginals625(); yield return null; }
            Check(NativeTemplateStore625("NativeTemplateBases").Count == 3,
                "actual original widget and already prepared party-card bases warm through production preparation");
            uint revision = TownServiceMirror.NativeTemplatePreparationRevision;
            TownServiceMirror.ResetNetwork();
            Check(TownServiceMirror.Assets.Generation == assets
                && TownServiceMirror.NativeTemplatePreparationRevision != revision
                && NativeTemplateStore625("NativeTemplateBases").Count == 0,
                "network reset retires only mirror bases while original asset identities survive");
            for (int frame = 0; frame < 16; frame++)
            { NativeTemplates.FixturePrepareOriginals625(); yield return null; }
            Check(NativeTemplateStore625("NativeTemplateBases").Count == 3
                && NativeTemplates.FixtureSameOriginal625(widget),
                "network reset rewarms existing frozen originals without changing asset identities");
            TownServiceFrame picture = NativeFrame623(widget, 12, 1, "face.62512|");
            // This source remains under its inactive original bank. Read authored
            // graphics without activating it, as the production basis does; the
            // ordinary live-owner sampling proof is separate above.
            using (var original = new TownServiceBinding(widget))
                picture.Nodes = original.Read(TownServiceMirror.Assets, includeInactiveGraphics: true);
            Check(TownServiceMirror.TryWriteNativeTemplateState(picture, out byte[] packet)
                && TownServiceCodec.TryRead(packet, packet.Length, out TownServiceFrame? sparse)
                && TownServiceMirror.TryExpandNativeTemplateState(sparse!, out _),
                "reset-warmed first offer reconstructs the genuine original immediately");
            File.WriteAllText(Path.Combine(_output, "native-template-reset625.txt"),
                "Actual ResetNetwork, same Assets.Generation, production preparation requeues existing widget/face/card originals.\n");
        }
        finally { NativeTemplates.FixtureClearFrozenBank625(); }
    }
}

// Only native game availability/model ports are adapters. The complete production
// preparation file, real immutable mirror registration, read/hash/expand, disconnect
// and receive/tick paths compile unchanged for the focused lifecycle proof.
namespace GloomhavenVR.WorldUI
{
    internal static partial class NativeTemplates
    {
        internal sealed partial class Part
        { internal Transform Original = null!; internal readonly HashSet<Transform> Excluded = new();
          internal Transform? NativeRingRoot; internal float NativeRingRate; }
        private sealed class Entry { internal readonly List<Part> Parts = new(); }
        private static readonly Dictionary<string, Entry> Entries = new();
        private static GameObject? _bank;
        private static void EnsureCard(string key)
        { if (!Entries.ContainsKey(key)) throw new InvalidOperationException("No actual frozen card at native port: " + key); }
        internal static void FixtureInstallFrozenBank625(GameObject bank, Transform actualOriginal, int id)
        {
            ResetEnhancementPreparation(); Entries.Clear();
            _bank = bank; _bank.SetActive(false);
            actualOriginal.SetParent(_bank.transform, false);
            foreach (string key in new[] { "enchant.inventory", "face." + id, "card." + id })
            { var entry = new Entry(); entry.Parts.Add(new Part { Original = actualOriginal }); Entries.Add(key, entry); }
            EnhancementPrepared.Add(id);
        }
        internal static bool FixtureSameOriginal625(Transform original) =>
            Entries["enchant.inventory"].Parts[0].Original == original;
        internal static void FixturePrepareOriginals625() => PrepareEnhancementOriginals();
        internal static void FixtureClearFrozenBank625()
        { ResetEnhancementPreparation(); Entries.Clear(); _bank = null; }
    }
    internal static partial class TownServiceNativeAssets
    { internal static void PrepareCard(ScenarioRuleLibrary.CAbilityCard? card) { } }
}
namespace GloomhavenVR.Cards
{ internal static class CardArtPin { internal static void PinForCard(ScenarioRuleLibrary.CAbilityCard card) { } } }
namespace ScenarioRuleLibrary
{ internal sealed class CAbilityCard { internal int ID; } }
internal static class CharacterClassManager
{ internal static readonly List<ScenarioRuleLibrary.CAbilityCard> AllAbilityCards = new(); }
internal static class ObjectPool { internal static readonly object instance = new(); }
namespace MapRuleLibrary.Adventure
{
    internal sealed class PreparationCharacter625 { internal int[]? HandAbilityCardIDs; }
    internal sealed class PreparationParty625 { internal PreparationCharacter625[]? SelectedCharactersArray; }
    internal sealed class PreparationMap625 { internal PreparationParty625? MapParty; }
    internal static class AdventureState { internal static PreparationMap625? MapState; }
}
