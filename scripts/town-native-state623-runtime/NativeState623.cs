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

    private static string NativePlaybackState623(int peer)
    {
        var detail = new System.Text.StringBuilder(" owner=" + TownServiceMirror.InteractionOwner(3));
        if (!TownServiceMirror.RemoteSessions.TryGetValue(peer, out TownServiceSessionInfo session))
            return detail + " session=absent";
        detail.Append(" session=" + session.Session + " active=" + session.Active
            + " staleAge=" + (Time.unscaledTime - session.LastSeenTime).ToString("F3"));
        var pending = (Dictionary<int, Dictionary<ushort, TownServiceFrame>>)typeof(TownServiceMirror)
            .GetField("Pending", PrivateStatic)!.GetValue(null)!;
        pending.TryGetValue(peer, out Dictionary<ushort, TownServiceFrame>? received);
        foreach (ushort id in session.Modules)
        {
            TownServiceFrame? frame = null;
            received?.TryGetValue(id, out frame);
            detail.Append(" module=" + id + ":pending=" + (frame?.Sequence.ToString() ?? "absent")
                + ":base=" + (frame?.BaseSequence.ToString() ?? "absent")
                + ":native=" + (frame?.NativeTemplateBasisKey.ToString("X") ?? "absent")
                + ":admitted=" + (Remote(peer, id) != null));
        }
        detail.Append(" reports=" + string.Join(" | ", GloomhavenVR.Core.VRLog.Messages));
        return detail.ToString();
    }

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
        TownServiceMirror.PrepareNativeTemplateBasis(3, "enchant.inventory|");
        var warmedBases = (IDictionary)typeof(TownServiceMirror).GetField("NativeTemplateBases", PrivateStatic)!.GetValue(null)!;
        Check(warmedBases.Count != 0, "actual inactive original binding and material descriptors warm before first drop");

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
        File.WriteAllText(Path.Combine(_output, "native-state625-initial-wire-cost.txt"),
            "Original bytes=" + full.Length + "; robust native bytes=" + unchanged.Length + "\n");
        Check(unchanged.Length < full.Length,
            "unchanged original graphic descriptors are omitted rather than resent for every upgrade widget");

        // A real peer can localize original template captions independently. Root
        // sibling indices also differ with freeze order. Owner output must win.
        var templates = (Dictionary<string, GameObject>)typeof(TownServiceMirror).GetField("Templates", PrivateStatic)!.GetValue(null)!;
        string key = (string)typeof(TownServiceMirror).GetMethod("TemplateKey", PrivateStatic)!
            .Invoke(null, new object[] { (byte)3, (ushort)17, "enchant.inventory|" })!;
        templates[key].transform.Find("Name").GetComponent<TextMeshProUGUI>().text = "Observer localized default";
        templates[key].transform.Find("Price").GetComponent<Text>().text = "Lokaler Standard";
        RectTransform observerFill = (RectTransform)templates[key].transform.Find("Filled");
        observerFill.sizeDelta *= 1.7f; observerFill.anchoredPosition += new Vector2(14, -12);
        observerFill.GetComponent<Image>().color = Color.blue;
        observerFill.GetComponent<Image>().fillAmount = .99f;
        templates[key].transform.Find("Background").gameObject.SetActive(false);
        typeof(TownServiceMirror).GetMethod("ResetNativeTemplateState", PrivateStatic)!.Invoke(null, null);
        Check(TownServiceMirror.TryExpandNativeTemplateState(sparse!, out TownServiceFrame expanded),
            "localized observer defaults never replace exact owner text or font");
        AssertNativeEqual623(original, expanded);
        Check(TownServiceMirror.TryExpandNativeTemplateState(sparse!, out expanded),
            "viewer native layout defaults never block a fully owner-authored original");

        // The immutable basis remains original; only actual owner changes travel.
        _text.text = "Owner exact selected upgrade"; _text.fontSize = 33;
        _fill.fillAmount = .31f; _fill.color = Color.magenta;
        _clip.padding = new Vector4(11, 7, 5, 3);
        source.Find("Viewport/Oversized").localPosition += new Vector3(0, 17, 0);
        var ownerMaterial = new Material(_fill.material) { color = Color.green };
        Assets.Add(ownerMaterial); _fill.material = ownerMaterial;
        TownServiceFrame changed = NativeFrame623(source, 10, 17, "enchant.inventory|"); changed.Sequence = 2;
        Check(TownServiceMirror.TryWriteNativeTemplateState(changed, out byte[] current),
            "actual native hover, scroll, clipping and owner text create a fresh metadata patch");
        Check(TownServiceCodec.TryRead(current, current.Length, out TownServiceFrame? currentSparse)
            && TownServiceMirror.TryExpandNativeTemplateState(currentSparse!, out expanded),
            "current metadata reconstructs a complete original without previous network state");
        AssertNativeEqual623(changed, expanded);
        var observerMaterial = new Material(observerFill.GetComponent<Image>().material) { color = Color.red };
        Assets.Add(observerMaterial); observerFill.GetComponent<Image>().material = observerMaterial;
        typeof(TownServiceMirror).GetMethod("ResetNativeTemplateState", PrivateStatic)!.Invoke(null, null);
        Check(TownServiceMirror.TryExpandNativeTemplateState(currentSparse!, out expanded),
            "a fully transmitted owner material replaces a different observer native default immediately");
        AssertNativeEqual623(changed, expanded);
        // The old unchanged packet legitimately depends on the original default
        // material. Restore that original before testing reordered old samples.
        observerFill.GetComponent<Image>().material = null;
        typeof(TownServiceMirror).GetMethod("ResetNativeTemplateState", PrivateStatic)!.Invoke(null, null);
        var bank = new TownServiceFrame { Service = 1, PublicCatalog = true, PublicClaim = 2,
            Session = changed.Session, Sequence = changed.Sequence, Module = changed.Module,
            Template = changed.Template, TemplateAddress = changed.TemplateAddress, Structure = changed.Structure,
            Visible = true, Pose = changed.Pose, Nodes = changed.Nodes };
        TownServiceMirror.RegisterTemplate(1, 17, source, address: "enchant.inventory|");
        Check(TownServiceMirror.TryWriteNativeTemplateState(bank, out byte[] publicPart)
            && TownServiceCodec.TryRead(publicPart, publicPart.Length, out TownServiceFrame? publicSparse)
            && publicSparse!.PublicCatalog && publicSparse.NativeTemplateBasisKey != 0,
            "public original cabinet members share the same compact native representation");

        TownServiceFrame confirmation = TownServiceDelta.Retain(changed);
        confirmation.Service = 1; confirmation.TemplateAddress = "item.confirm.part.2|";
        TownServiceMirror.RegisterTemplate(1, 17, source, address: confirmation.TemplateAddress);
        TownServiceMirror.PrepareNativeTemplateBasis(1, confirmation.TemplateAddress);
        Check(TownServiceMirror.TryWriteNativeTemplateState(confirmation, out byte[] decision)
            && TownServiceCodec.TryRead(decision, decision.Length, out TownServiceFrame? originalDecision)
            && !originalDecision!.PublicCatalog && originalDecision.NativeTemplateBasisKey != 0
            && TownServiceMirror.TryExpandNativeTemplateState(originalDecision, out _),
            "private merchant confirmation uses immediately reconstructed exact native metadata");
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
        // Both endpoints are represented in this process. Await the actual existing
        // lease while the face is missing, rather than assuming editor scheduling
        // settled it within a fixed delay. The final one-pass admission remains strict.
        float leaseDeadline = Time.unscaledTime + 1f;
        while (TownServiceMirror.InteractionOwner(3) != 2 && Time.unscaledTime < leaseDeadline)
        {
            TownServiceMirror.TickRemote(_ => observer);
            Check(Remote(2, 10) == null && Remote(2, 12) == null,
                "actual playback never exposes option holes or a ring before the complete offered card");
            yield return null;
        }
        Check(TownServiceMirror.InteractionOwner(3) == 2,
            "actual remote mage lease settles before missing-card admission" + NativePlaybackState623(2));
        TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(2, 10) == null && Remote(2, 12) == null,
            "actual playback never exposes option holes or a ring before the complete offered card");
        Receive(2, new[] { TownServiceCodec.Write(offered) });
        TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(2, 10) != null && Remote(2, 11) != null && Remote(2, 12) != null,
            "one actual playback pass presents the complete original mage picture" + NativePlaybackState623(2));
        Check(Remote(2, 10)!.Root.Find("Name").GetComponent<TextMeshProUGUI>().text == _text.text,
            "actual compact receiver preserves the owner upgrade text");
        Check(GameplayFixture.Awakes == awakes && GameplayFixture.Enables == enables,
            "native bases and original playback never run gameplay Awake or OnEnable callbacks");
        File.WriteAllText(Path.Combine(_output, "native-state623-wire-cost.txt"),
            "Original bytes=" + full.Length + "; unchanged native bytes=" + unchanged.Length
            + "; changed native bytes=" + current.Length + "; complete native nodes=" + changed.Nodes.Length + "\n");
        IEnumerator coldProof = NativeColdOriginal625(author, observer);
        while (coldProof.MoveNext()) yield return coldProof.Current;
        IEnumerator deltaProof = NativeQueuedDelta626(author, observer);
        while (deltaProof.MoveNext()) yield return deltaProof.Current;
        IEnumerator bankProof = NativeBankSplit623(); while (bankProof.MoveNext()) yield return bankProof.Current;
        IEnumerator deliveryProof = NativeDelivery629(); while (deliveryProof.MoveNext()) yield return deliveryProof.Current;
        yield return null;
    }

    private static IEnumerator NativeQueuedDelta626(Transform author, Transform observer)
    {
        TownServiceMirror.Shutdown(); Baselines.Clear();
        GloomhavenVR.Net.NetPlayerActors.Peer = 10;
        Transform original = Source(author);
        const string address = "enchant.inventory|";
        TownServiceMirror.RegisterTemplate(3, 1, original, address: address);
        TownServiceMirror.BeginSession(3, 626, author, author);
        TownServiceMirror.RegisterModule(41, 1, original, address: address);
        TownServiceMirror.SetPriority(41, true);
        var wire = new TownServiceLaneSendQueue(0);
        var fragments = new TownServiceFragments();
        var samples = new List<TownServiceFrame>();
        var lengths = new List<int>();
        double clock = 0;
        Action<byte[], int, object?> publish = (bytes, length, metadata) =>
        {
            Check(metadata is TownServiceFrame, "actual first-offer capture retains original queue metadata");
            var frame = (TownServiceFrame)metadata!;
            if (frame.Module != TownServiceFrame.ManifestModule)
            {
                Check(TownServiceCodec.TryRead(bytes, length, out TownServiceFrame? decoded),
                    "actual original capture uses the production native codec");
                samples.Add(decoded!); lengths.Add(length);
            }
            wire.Enqueue(bytes, length, frame);
        };
        Action capture = () => typeof(TownServiceMirror).GetMethod("CaptureCore", PrivateStatic)!
            .Invoke(null, new object[] { publish, false });
        Action pump = () =>
        {
            int idle = 0;
            for (int turn = 0; turn < 512 && idle < 3; turn++)
            {
                clock += .050001;
                byte[]? page = wire.Next(clock);
                if (page == null) { idle++; continue; }
                idle = 0;
                Check(page.Length <= ExtrasFragments.MaxDatagramBytes,
                    "native first-offer and hover preserve the bounded datagram budget");
                byte[]? packet = fragments.Accept(2, page, page.Length, clock);
                if (packet == null) continue;
                byte[][] members = TownServiceCodec.TryReadBundle(packet, packet.Length, out byte[][]? bundle)
                    ? bundle! : new[] { packet };
                Receive(2, members);
            }
            Check(idle == 3, "native original queue drains without delayed periodic repair");
        };
        capture();
        Check(samples.Count == 1 && samples[0].BaseSequence == 0 && samples[0].NativeTemplateBasisKey == 0,
            "first captured enhancement original carries complete owner state without a template-repair wait");
        TownServiceFrame first = samples[0]; int initialBytes = lengths[0];
        pump();
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(2, 41) != null, "real first-offer queue builds its complete original on reception");

        _text.text = "Actual owner selected enhancement"; _text.fontSize += 1f;
        _fill.fillAmount = .43f; _fill.color = Color.cyan;
        _clip.padding = new Vector4(3f, 5f, 7f, 9f);
        original.Find("Viewport/Oversized").localPosition += new Vector3(0f, 11f, 0f);
        capture();
        Check(samples.Count == 2 && samples[1].BaseSequence == first.Sequence
            && samples[1].NativeTemplateBasisKey == 0,
            "after the exact native original actual hover capture uses the existing cumulative owner delta");
        TownServiceFrame changed = samples[1];
        Check(lengths[1] < initialBytes,
            "actual cumulative hover avoids repeating the complete owner native property table");
        Check(TownServiceDelta.Expand(null, changed) == null,
            "a missing genuine owner baseline never reconstructs hover from viewer defaults");
        pump(); TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(2, 41)!.Root.Find("Name").GetComponent<TextMeshProUGUI>().text == _text.text,
            "real cumulative native playback preserves selected upgrade owner text");
        // The existing native animation adapter preserves intermediate hover
        // movement instead of jumping from the previous original sample.
        for (float until = Time.unscaledTime + .3f; Time.unscaledTime < until;)
        { TownServiceMirror.TickRemote(_ => observer); yield return null; }
        Check(Remote(2, 41)!.Root.Find("Filled").GetComponent<Image>().color == _fill.color,
            "real cumulative native playback preserves owner hover color");
        Check(Remote(2, 41)!.Root.Find("Viewport").GetComponent<RectMask2D>().padding == _clip.padding,
            "real cumulative native playback preserves owner scroll clipping");

        // The delta may overtake its sparse original. Normal admission retains
        // that exact owner original even though its sequence is older.
        Check(TownServiceMirror.ReceiveParsed(4, changed), "reordered native hover is retained before its baseline");
        Check(TownServiceMirror.ReceiveParsed(4, first), "reordered native sparse baseline admits through original expansion");
        var received = (Dictionary<int, Dictionary<ushort, TownServiceFrame>>)typeof(TownServiceMirror)
            .GetField("ReceivedBaselines", PrivateStatic)!.GetValue(null)!;
        Check(received[4][41].Sequence == first.Sequence
            && TownServiceDelta.Expand(received[4][41], changed) != null,
            "late exact original unblocks its retained cumulative native hover");
        TownServiceMirror.RemovePeer(4);

        var modules = (IDictionary)typeof(TownServiceMirror).GetProperty("Local", PrivateStatic)!.GetValue(null)!;
        object module = modules[(ushort)41]!;
        module.GetType().GetField("NextBaseline", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(module, 0f);
        module.GetType().GetField("NextRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(module, 0f);
        capture();
        Check(samples.Count == 3 && samples[2].BaseSequence == 0,
            "periodic complete native repair still supplies a new exact owner baseline");
        pump(); TownServiceMirror.TickRemote(_ => observer);
        Check(Remote(2, 41)!.Root.Find("Name").GetComponent<TextMeshProUGUI>().text == _text.text,
            "periodic native repair cannot roll selected upgrade artwork back");
        File.WriteAllText(Path.Combine(_output, "native-queued-delta626.txt"),
            "Actual CaptureCore -> immutable queue -> native bundle/fragments -> ReceiveParsed -> TickRemote.\n"
            + "Initial original=" + initialBytes + " bytes; cumulative hover=" + lengths[1]
            + " bytes; current complete repair=" + lengths[2] + " bytes.\n"
            + "Reordered delta requires its exact independently expanded original baseline.\n");
    }

    private static IEnumerator NativeColdOriginal625(Transform author, Transform observer)
    {
        const string address = "face.62501|";
        Transform cold = Image("Actual late original offered card", author, Vector2.zero,
            new Vector2(.14f, .22f), Color.yellow).transform;
        cold.GetComponent<Image>().sprite = _fill.sprite;
        TownServiceMirror.RegisterTemplate(3, 25, cold, address: address);
        TownServiceFrame offered = NativeFrame623(cold, 25, 25, address);
        offered.Session = 625; offered.Sequence = 1002;
        Check(TownServiceMirror.TryWriteNativeTemplateState(offered, out byte[] packet),
            "first offered original produces independently reconstructable native metadata");
        var templates = (Dictionary<string, GameObject>)typeof(TownServiceMirror).GetField("Templates", PrivateStatic)!.GetValue(null)!;
        string key = (string)typeof(TownServiceMirror).GetMethod("TemplateKey", PrivateStatic)!
            .Invoke(null, new object[] { (byte)3, (ushort)25, address })!;
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
            var manifest = new TownServiceFrame { Service = 3, Session = 625, Sequence = 1001,
                Module = TownServiceFrame.ManifestModule, Visible = true, TransactionActive = true,
                Modules = new ushort[] { 25 }, Pose = offered.Pose };
            Receive(2, new[] { TownServiceCodec.Write(manifest), packet });
            for (float until = Time.unscaledTime + .15f; Time.unscaledTime < until;) yield return null;
            TownServiceMirror.TickRemote(_ => observer);
            Check(Remote(2, 25) == null, "missing genuine original never substitutes a grey or mismatched offered card");
            ready = true;
            for (float until = Time.unscaledTime + .10f; Remote(2, 25) == null && Time.unscaledTime < until;)
            { TownServiceMirror.TickRemote(_ => observer); yield return null; }
            Check(Remote(2, 25) != null,
                "a retained first sparse offer replays immediately after its original becomes available");
            Check(Remote(2, 25)!.Root.GetComponent<Image>().color == Color.yellow,
                "deferred first offer enters through the same exact native receiver");

            // The old compact sample is not allowed to overwrite a later full
            // original, even if template preparation completes after that full packet.
            TownServiceFrame newer = TownServiceDelta.Retain(offered); newer.Sequence = 1004;
            newer.Nodes[0] = new TownServiceNode { Binding = offered.Nodes[0].Binding };
            foreach (var property in offered.Nodes[0].Values) newer.Nodes[0].Values.Add(property.Key, property.Value);
            newer.Nodes[0].Values[TownServiceProperty.Graphic] = new TownServiceValue { Numbers = new[] { 1f, 1f, 0f, 1f, 1f } };
            templates.Remove(key); ready = false;
            typeof(TownServiceMirror).GetMethod("ResetNativeTemplateState", PrivateStatic)!.Invoke(null, null);
            Receive(2, new[] { packet, TownServiceCodec.Write(newer) });
            ready = true; templates[key] = frozen;
            for (float until = Time.unscaledTime + .25f; Time.unscaledTime < until;)
            { TownServiceMirror.TickRemote(_ => observer); yield return null; }
            Check(Remote(2, 25)!.Root.GetComponent<Image>().color == Color.magenta,
                "late native preparation cannot replace a newer accepted original with its old offer");
        }
        finally { TownServiceMirror.ResolveTemplate = previous; templates[key] = frozen; }
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
            original.HasCanvasFrame = true;
            original.CanvasRect = new[] { 400f, 60f, .5f, .5f };
            original.CanvasSettings = new[] { 1f, 0f, 0f, 0f, 0f };
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
        long legacyAllocation = GC.GetAllocatedBytesForCurrentThread();
        var legacyClock = System.Diagnostics.Stopwatch.StartNew();
        for (int measure = 0; measure < 4; measure++)
            try { TownServiceCodec.Write(root); }
            catch (InvalidDataException error) when (error.Message == "Original catalog updates exceed the packed bank bound.") { }
        legacyClock.Stop(); legacyAllocation = GC.GetAllocatedBytesForCurrentThread() - legacyAllocation;
        long clockAllocation = 0; double clockMs = 0;
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
            reference = PreparedCatalogPacket625(root, send);
            Check(repairs.Count == 0, "prepared catalog publication performs no synchronous original repair work");
            long before = GC.GetAllocatedBytesForCurrentThread();
            var measured = System.Diagnostics.Stopwatch.StartNew();
            for (int measure = 0; measure < 32; measure++)
                reference = PreparedCatalogPacket625(root, send);
            measured.Stop(); clockAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
            clockMs = measured.Elapsed.TotalMilliseconds / 32;
            Check(legacyAllocation == 0 || clockAllocation / 32 < legacyAllocation / 4 / 4,
                "prepared page clock allocates less than one quarter of synchronous full native bank encoding");
            // Publication retains exact original dependencies as metadata. The
            // transport owns their bounded delivery; exercise their real native
            // writer independently here before the receiver admission checks.
            foreach (TownServiceFrame original in root.CatalogBank.Updates)
                repairs.Add((byte[])typeof(TownServiceMirror).GetMethod("WriteNativeTownFrame", PrivateStatic)!
                    .Invoke(null, new object[] { original })!);
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
        TownServiceFrame forged = TownServiceDelta.Retain(clock!);
        forged.Sequence++;
        forged.CatalogBank!.Headers[0].CanvasSortingOrder++;
        byte[] forgedBytes = TownServiceCodec.Write(forged);
        Check(!TownServiceMirror.Receive(2, forgedBytes, forgedBytes.Length),
            "cached original key cannot authorize a changed canonical canvas header");
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
            "Packed original bank exceeded 56320 B; independent repairs=" + repairs.Count
            + "; reference=" + reference.Length + " B; current headers=" + clock!.CatalogBank!.Headers.Length
            + "; no dependency loss; final repair admits complete original catalog\n"
            + "Full native bank encoder=" + (legacyClock.Elapsed.TotalMilliseconds / 4).ToString("F3")
            + " ms and " + (legacyAllocation / 4) + " B/call; prepared original clock="
            + clockMs.ToString("F3") + " ms and " + (clockAllocation / 32) + " B/call\n");
        yield return null;
    }

    private static byte[] PreparedCatalogPacket625(TownServiceFrame root, Action<byte[], int, object?> send)
    {
        try
        {
            return (byte[])typeof(TownServiceMirror).GetMethod("WriteCatalogPacket", PrivateStatic)!
                .Invoke(null, new object[] { root, send })!;
        }
        catch (TargetInvocationException error) when (error.InnerException is InvalidDataException)
        {
            Check(false, "prepared page clock excludes synchronous full-bank compression");
            throw;
        }
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
