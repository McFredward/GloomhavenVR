using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private static TownServiceFrame NativeFrame623(Transform source, ushort module, ushort template, string address)
    {
        using var binding = new TownServiceBinding(source);
        return new TownServiceFrame { Service = 3, Session = 623, Sequence = 1, Module = module,
            Template = template, TemplateAddress = address, Structure = binding.Structure,
            Visible = true, Nodes = binding.Read(TownServiceMirror.Assets),
            Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } };
    }

    private static bool NativePicture623(TownServiceSessionInfo session, Dictionary<ushort, TownServiceFrame> pending) =>
        (bool)typeof(TownServiceMirror).GetMethod("MagePictureReady", PrivateStatic)!
            .Invoke(null, new object[] { 2, session, pending })!;

    private static IEnumerator NativeState623()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear();
        Transform author = Go("native-state author").transform;
        Transform observer = Go("native-state observer").transform;
        Transform source = Source(author);
        for (int i = 0; i < 24; i++)
        {
            Image graphic = Image("Original decorative frame " + i, source, new Vector2(i, -i),
                new Vector2(20, 30), Color.white);
            graphic.sprite = _fill.sprite;
        }
        Canvas.ForceUpdateCanvases();
        TownServiceMirror.RegisterTemplate(3, 17, source, address: "enchant.inventory|");
        Transform face = Image("Actual original offered face", author, Vector2.zero,
            new Vector2(.14f, .22f), Color.yellow).transform;
        face.GetComponent<Image>().sprite = _fill.sprite;
        TownServiceMirror.RegisterTemplate(3, 18, face, address: "face.62301|");
        Transform ring = Image("Actual original native highlight", author, Vector2.zero,
            new Vector2(.16f, .24f), Color.cyan).transform;
        TownServiceMirror.RegisterTemplate(3, 19, ring, address: "enchant.highlight|");
        int awakes = GameplayFixture.Awakes, enables = GameplayFixture.Enables;

        TownServiceFrame original = NativeFrame623(source, 10, 17, "enchant.inventory|");
        bool encodedOriginal = TownServiceMirror.TryWriteNativeTemplateState(original, out byte[] unchanged);
        if (!encodedOriginal)
        {
            object? basis = typeof(TownServiceMirror).GetMethod("NativeBasis", PrivateStatic)!.Invoke(null, new object[] { original });
            var details = new System.Text.StringBuilder();
            details.AppendLine("Source structure=" + original.Structure + "; nodes=" + original.Nodes.Length);
            if (basis != null)
            {
                var native = (TownServiceNode[])basis.GetType().GetField("Nodes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(basis)!;
                details.AppendLine("Basis structure=" + basis.GetType().GetField("Structure", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(basis) + "; nodes=" + native.Length);
                for (int n = 0; n < Math.Min(native.Length, original.Nodes.Length); n++)
                    details.AppendLine(n + ": native=" + native[n].Binding + " (" + string.Join(",", native[n].Values.Keys) + "); source=" + original.Nodes[n].Binding + " (" + string.Join(",", original.Nodes[n].Values.Keys) + ")");
            }
            details.AppendLine(string.Join("\n", GloomhavenVR.Core.VRLog.Messages));
            File.WriteAllText(Path.Combine(_output,"native-basis-mismatch.txt"), details.ToString());
        }
        Check(encodedOriginal, "actual original prefab produces compact native metadata without a prior network baseline");
        Check(TownServiceCodec.TryRead(unchanged, unchanged.Length, out TownServiceFrame? sparse)
            && sparse!.NativeTemplateBasisKey != 0 && sparse.BaseSequence == 0,
            "native metadata remains a complete independent original module");
        byte[] full = TownServiceCodec.Write(original);
        Check(unchanged.Length < full.Length / 2,
            "unchanged original graphic descriptors are omitted rather than resent for every upgrade widget");

        // A real peer can localize original template captions independently. Root
        // sibling indices also differ with freeze order. Owner output must win.
        var templates = (Dictionary<string, GameObject>)typeof(TownServiceMirror).GetField("Templates", PrivateStatic)!.GetValue(null)!;
        string key = (string)typeof(TownServiceMirror).GetMethod("TemplateKey", PrivateStatic)!
            .Invoke(null, new object[] { (byte)3, (ushort)17, "enchant.inventory|" })!;
        templates[key].transform.Find("Name").GetComponent<TextMeshProUGUI>().text = "Observer localized default";
        templates[key].transform.Find("Price").GetComponent<Text>().text = "Lokaler Standard";
        typeof(TownServiceMirror).GetMethod("ResetNativeTemplateState", PrivateStatic)!.Invoke(null, null);
        Check(TownServiceMirror.TryExpandNativeTemplateState(sparse!, out TownServiceFrame expanded),
            "localized observer defaults never replace exact owner text or font");
        AssertNativeEqual623(original, expanded);

        // The immutable basis remains original; only actual owner changes travel.
        _text.text = "Owner exact selected upgrade"; _text.fontSize = 33;
        _fill.fillAmount = .31f; _fill.color = Color.magenta;
        _clip.padding = new Vector4(11, 7, 5, 3);
        source.Find("Viewport/Oversized").localPosition += new Vector3(0, 17, 0);
        TownServiceFrame changed = NativeFrame623(source, 10, 17, "enchant.inventory|"); changed.Sequence = 2;
        Check(TownServiceMirror.TryWriteNativeTemplateState(changed, out byte[] current),
            "actual native hover, scroll, clipping and owner text create a fresh metadata patch");
        Check(TownServiceCodec.TryRead(current, current.Length, out TownServiceFrame? currentSparse)
            && TownServiceMirror.TryExpandNativeTemplateState(currentSparse!, out expanded),
            "current metadata reconstructs a complete original without previous network state");
        AssertNativeEqual623(changed, expanded);
        var bank = new TownServiceFrame { Service = 1, PublicCatalog = true, PublicClaim = 2,
            Session = changed.Session, Sequence = changed.Sequence, Module = changed.Module,
            Template = changed.Template, TemplateAddress = changed.TemplateAddress, Structure = changed.Structure,
            Visible = true, Pose = changed.Pose, Nodes = changed.Nodes };
        TownServiceMirror.RegisterTemplate(1, 17, source, address: "enchant.inventory|");
        Check(TownServiceMirror.TryWriteNativeTemplateState(bank, out byte[] publicPart)
            && TownServiceCodec.TryRead(publicPart, publicPart.Length, out TownServiceFrame? publicSparse)
            && publicSparse!.PublicCatalog && publicSparse.NativeTemplateBasisKey != 0,
            "public original cabinet members share the same compact native representation");

        byte[] bundle = TownServiceCodec.WriteBundle(new[] { current, unchanged });
        Check(TownServiceCodec.TryReadBundle(bundle, bundle.Length, out byte[][]? unpacked) && unpacked!.Length == 2,
            "actual production bundle retains sparse native metadata members");
        foreach (byte[] member in unpacked!)
        {
            Check(TownServiceCodec.TryRead(member, member.Length, out TownServiceFrame? decoded)
                && decoded!.NativeTemplateBasisKey != 0
                && TownServiceMirror.TryExpandNativeTemplateState(decoded, out _),
                "bundled native metadata reconstructs through the real original template path");
        }
        TownServiceFrame bad = TownServiceDelta.Retain(currentSparse!); bad.NativeTemplateBasisKey ^= 1;
        Check(!TownServiceMirror.TryExpandNativeTemplateState(bad, out _),
            "a genuinely different original basis is rejected before changing observer widgets");
        bad = TownServiceDelta.Retain(currentSparse!);
        var first = new TownServiceNode { Binding = bad.Nodes[0].Binding };
        foreach (var property in bad.Nodes[0].Values)
            if (property.Key != TownServiceProperty.Active) first.Values.Add(property.Key, property.Value);
        bad.Nodes[0] = first;
        Check(!TownServiceMirror.TryExpandNativeTemplateState(bad, out _),
            "missing owner root state cannot be replaced by observer template defaults");

        TownServiceFrame offered = NativeFrame623(face, 11, 18, "face.62301|");
        TownServiceFrame aura = NativeFrame623(ring, 12, 19, "enchant.highlight|");
        var pending = new Dictionary<ushort, TownServiceFrame> { [10] = changed, [12] = aura };
        var session = new TownServiceSessionInfo { Peer = 2, Service = 3, Session = 623, Active = true,
            Modules = new ushort[] { 10, 11, 12 } };
        Check(!NativePicture623(session, pending),
            "a missing offered card prevents a partial ring or options picture");
        pending[11] = offered;
        Check(NativePicture623(session, pending),
            "complete original card, native aura and every upgrade option are admitted together");
        pending.Remove(10);
        Check(!NativePicture623(session, pending),
            "a delayed original option branch blocks partial admission after the offered face arrives");
        pending[10] = changed;
        Check(NativePicture623(session, pending),
            "the complete original option list becomes ready in one pass after its final dependency");

        // Exercise the actual receiver, not just the readiness predicate. The
        // manifest and aura arrive first, but no incomplete new mage picture may
        // become visible. Then one Tick must admit every original module.
        var manifest = new TownServiceFrame { Service = 3, Session = 623, Sequence = 1,
            Module = TownServiceFrame.ManifestModule, Visible = true,
            TransactionActive = true, Modules = session.Modules, Pose = offered.Pose };
        Receive(2, new[] { TownServiceCodec.Write(manifest), TownServiceCodec.Write(aura), current });
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        // Both endpoints are represented in this process. Let the existing lease
        // settle while the face is missing, before measuring atomic admission.
        for (float until = Time.unscaledTime + .15f; Time.unscaledTime < until;) yield return null;
        TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(2, 10) == null && Remote(2, 12) == null,
            "actual playback never exposes option holes or a ring before the complete offered card");
        Receive(2, new[] { TownServiceCodec.Write(offered) });
        TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(2, 10) != null && Remote(2, 11) != null && Remote(2, 12) != null,
            "one actual playback pass presents the complete original mage picture");
        Check(Remote(2, 10)!.Root.Find("Name").GetComponent<TextMeshProUGUI>().text == _text.text,
            "actual compact receiver preserves the owner upgrade text");
        Check(GameplayFixture.Awakes == awakes && GameplayFixture.Enables == enables,
            "native bases and original playback never run gameplay Awake or OnEnable callbacks");
        File.WriteAllText(Path.Combine(_output, "native-state623-wire-cost.txt"),
            "Original bytes=" + full.Length + "; unchanged native bytes=" + unchanged.Length
            + "; changed native bytes=" + current.Length + "; complete native nodes=" + changed.Nodes.Length + "\n");
        IEnumerator bankProof = NativeBankSplit623(); while (bankProof.MoveNext()) yield return bankProof.Current;
        yield return null;
    }

    private static IEnumerator NativeBankSplit623()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); GloomhavenVR.Net.NetPlayerActors.Peer = 1;
        Transform author = Go("oversized original catalog author").transform;
        Transform viewer = Go("oversized original catalog observer").transform;
        Transform rack = Go("Original native oversized rack", author).transform;
        Transform crank = Go("Original native oversized crank", rack).transform;
        TownServiceMirror.RegisterTemplate(1, 1, rack, child => child == crank, "merchant.rack|");
        TownServiceMirror.RegisterTemplate(1, 1, crank, address: "merchant.crank|");
        TownServiceFrame root = NativeFrame623(rack, 200, 1, "merchant.rack|");
        root.Service = 1; root.PublicCatalog = true; root.PublicClaim = 1; root.Session = 624;
        root.Sequence = 100;
        using (var rootBinding = new TownServiceBinding(rack, child => child == crank))
        { root.Nodes = rootBinding.Read(TownServiceMirror.Assets); root.Structure = rootBinding.Structure; }
        root.Rack = new TownRackState { Crank = 201, Turn = 9, Members = new TownRackMember[8] };
        var originals = new TownServiceFrame[8]; var references = new TownCatalogBankMember[8];
        var random = new System.Random(623); const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        for (int i = 0; i < originals.Length; i++)
        {
            ushort id = (ushort)(210 + i);
            Transform row = Rect("Original native catalog inscription", author, Vector2.zero, new Vector2(400, 60));
            var label = row.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset; label.richText = false; label.fontSize = 20 + i;
            var text = new char[12000];
            for (int j = 0; j < text.Length; j++) text[j] = letters[random.Next(letters.Length)];
            label.text = new string(text);
            TownServiceMirror.RegisterTemplate(1, 1, row, address: "merchant.row|");
            TownServiceFrame original = NativeFrame623(row, id, 1, "merchant.row|");
            original.Service = 1; original.PublicCatalog = true; original.PublicClaim = root.PublicClaim;
            original.Session = root.Session; original.Sequence = (ulong)(2 + i);
            original.RackMember = new TownRackStamp { Rack = root.Module, Turn = root.Rack.Turn };
            originals[i] = original; references[i] = new TownCatalogBankMember(id, TownCatalogBank.ContentKey(original));
            root.Rack.Members[i] = new TownRackMember(id, 0, false);
        }
        root.CatalogBank = new TownCatalogBank { Prepared = true, Members = references, Updates = originals };
        bool packedOverflow = false;
        try { TownServiceCodec.Write(root); }
        catch (InvalidDataException error) { packedOverflow = error.Message == "Original catalog updates exceed the packed bank bound."; }
        Check(packedOverflow, "actual captured original catalog exceeds the packed bank envelope");
        var repairs = new List<byte[]>(); byte[] reference;
        using (TownServiceMirror.UsePublicLane())
        {
            TownServiceMirror.BeginSession(1, root.Session, author, rack);
            TownServiceMirror.SetCatalogBankPrepared(root.Module, true);
            Action<byte[], int, object?> send = (bytes, length, identity) =>
            {
                Check(bytes.Length == length && identity is TownServiceFrame tagged && tagged.HighPriority,
                    "actual bank splitter emits complete independently tagged original repairs");
                repairs.Add(bytes);
            };
            reference = (byte[])typeof(TownServiceMirror).GetMethod("WriteCatalogPacket", PrivateStatic)!
                .Invoke(null, new object[] { root, send })!;
            TownServiceMirror.EndSession();
        }
        Check(repairs.Count == originals.Length, "oversized native catalog retains every exact original repair");
        Check(TownServiceCodec.TryRead(reference, reference.Length, out TownServiceFrame? clock)
            && clock!.CatalogBank!.Prepared && clock.CatalogBank.Updates.Length == 0
            && clock.CatalogBank.Headers.Length == originals.Length,
            "split reference retains every current original header for atomic dependency admission");
        for (int i = 0; i < repairs.Count; i++)
        {
            Check(TownServiceCodec.TryRead(repairs[i], repairs[i].Length, out TownServiceFrame? sparse)
                && TownServiceMirror.TryExpandNativeTemplateState(sparse!, out _),
                "split original repair preserves native metadata through the real codec");
            TownServiceCodec.TryRead(repairs[i], repairs[i].Length, out sparse);
            TownServiceMirror.TryExpandNativeTemplateState(sparse!, out TownServiceFrame actual);
            AssertNativeEqual623(originals[i], actual);
            Check(TownCatalogBank.ContentKey(actual) == references[i].ContentKey,
                "every split original retains its exact content identity");
        }
        GloomhavenVR.Net.NetPlayerActors.Peer = 10;
        Check(!TownServiceMirror.Receive(2, reference, reference.Length),
            "reference clock cannot expose a catalog before its original repairs arrive");
        for (int i = 0; i < repairs.Count - 1; i++) Receive(2, new[] { repairs[i] });
        Check(!TownServiceMirror.Receive(2, reference, reference.Length),
            "one missing original keeps the complete prepared catalog unadmitted");
        Receive(2, new[] { repairs[repairs.Count - 1] });
        Check(TownServiceMirror.Receive(2, reference, reference.Length),
            "actual split bank admits immediately after its final exact original repair");
        var pending = (Dictionary<int, Dictionary<ushort, TownServiceFrame>>)typeof(TownServiceMirror)
            .GetField("Pending", PrivateStatic)!.GetValue(null)!;
        Check(pending.TryGetValue(-2, out var full) && full.ContainsKey(root.Module),
            "admitted reference retains the actual full prepared catalog root");
        foreach (TownServiceFrame original in originals)
        {
            Check(full!.TryGetValue(original.Module, out TownServiceFrame? restored),
                "all repaired original branches enter the exact prepared picture");
            AssertNativeEqual623(original, restored!);
        }
        File.WriteAllText(Path.Combine(_output, "native-bank-split623.txt"),
            "Packed original bank exceeded 56320 B; repairs=" + repairs.Count
            + "; reference=" + reference.Length + " B; current headers=" + clock!.CatalogBank!.Headers.Length
            + "; no dependency loss; final repair admits complete original catalog\n");
        yield return null;
    }

    private static void AssertNativeEqual623(TownServiceFrame original, TownServiceFrame reconstructed)
    {
        Check(reconstructed.Nodes.Length == original.Nodes.Length && reconstructed.NativeTemplateBasisKey == 0,
            "expanded native output restores every original binding before playback");
        for (int i = 0; i < original.Nodes.Length; i++)
        {
            TownServiceNode before = original.Nodes[i], after = reconstructed.Nodes[i];
            Check(before.Binding == after.Binding && before.Values.Count == after.Values.Count,
                "original native property vocabulary and binding order are unchanged");
            foreach (var property in before.Values)
                Check(after.Values.TryGetValue(property.Key, out TownServiceValue? actual) && property.Value.Same(actual),
                    "every original native value, asset key, text style and animation state reconstructs exactly");
        }
    }
}
