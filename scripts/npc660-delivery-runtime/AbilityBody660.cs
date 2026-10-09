using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IEnumerator AbilityBody660()
    {
        AbilityBodyBorrowOrder660();
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 10;
        GameObject bank = Go("660 inactive canonical original bank"); bank.SetActive(false);
        Transform owner = Go("660 actual owner").transform, observer = Go("660 actual observer").transform;
        LazyTemplateProbe.Open(bank); CardsDriver.CardBackingPrefab = null;
        TownServiceMirror.SharedFrameForRemote = _ => observer;
        TownServiceMirror.ResolveTemplate = LazyTemplateProbe.Resolve;
        TownServiceMirror.PrepareInertGeometry = (address, clone) => TownServiceAbilityBody.RebindClone(address, clone);
        TownServiceMirror.CollectOriginalReceiptPeers = peers => {
            peers.Clear(); peers.Add(NetPlayerActors.Peer == 2 ? 10 : 2);
        };
        var publications = new List<TownServiceFrame>();
        try
        {
            foreach (Vector2 size in new[] { new Vector2(.0635f, .088f), new Vector2(.14f, .195f) })
            {
                GameObject wrapper = Go("660 local VRCard wrapper");
                VRCard card = wrapper.AddComponent<VRCard>();
                // These fields are the production readonly identity accessors'
                // actual backing/base size. Local geometry is the unmodified
                // production VRCard factory plus complete CardMesh/CardContour.
                Transform body = VRCard.BuildProceduralBacking(wrapper.transform, size.x, size.y);
                typeof(VRCard).GetField("_backing", PrivateInstance)!.SetValue(card, body);
                typeof(VRCard).GetField("_backingBaseSize", PrivateInstance)!.SetValue(card, size);
                typeof(VRCard).GetField("_proceduralBacking", PrivateInstance)!.SetValue(card, true);
                string key = TownServiceAbilityBody.Key(card);
                Check(key.EndsWith(".p", StringComparison.Ordinal), "actual null-prefab body uses its owner procedural identity");
                IReadOnlyList<LazyTemplateProbe.Part> parts = LazyTemplateProbe.Parts(key);
                Check(parts.Count == 1 && parts[0].Original.GetComponent<MeshFilter>() != null,
                    "actual null-prefab backing publishes its original physical mesh on first request");
                Check(!parts[0].Original.gameObject.activeInHierarchy,
                    "canonical ability body preparation never activates its original bank");
                Mesh local = body.GetComponent<MeshFilter>().sharedMesh;
                Mesh frozen = parts[0].Original.GetComponent<MeshFilter>().sharedMesh;
                Check(ReferenceEquals(local, frozen), "canonical body uses exact shared owner mesh at original dimensions");
                Check(parts[0].Original.GetComponent<MeshRenderer>().sharedMaterials.SequenceEqual(body.GetComponent<MeshRenderer>().sharedMaterials),
                    "canonical body uses exact original ability materials, no material surrogate");
                string address = key + "|";
                Check(LazyTemplateProbe.Resolve(3, 1, address), "first body request resolves its exact native original");
                TownServiceMirror.BeginSession(3, (uint)(size.x * 10000), owner, owner);
                TownServiceMirror.RegisterModule(1, 1, body, address: address);
                TownServiceMirror.SetPriority(1, true); TownServiceMirror.SetLocalTransactionActive(3, true);
                var scheduler = new ExtrasSendScheduler(0, 3, 4); var fragments = new TownServiceFragments();
                var watch = System.Diagnostics.Stopwatch.StartNew(); int events = 0;
                NetPlayerActors.Peer = 2; Sender660(true);
                try { TownServiceMirror.Capture((byte[] bytes, int length, object? identity) => {
                    if (identity is TownServiceFrame frame) publications.Add(frame);
                    if (TownServiceCodec.TryRead(bytes, length, out TownServiceFrame? sent) && sent!.Module == 1)
                        Check(sent.NativeTemplateBasisKey != 0,
                            "actual first physical body uses independent compact105 metadata, not a supplied full fixture original");
                    scheduler.Enqueue(bytes, length, identity: identity);
                }); }
                finally { Sender660(false); NetPlayerActors.Peer = 10; }
                while (watch.Elapsed.TotalSeconds < 1 && Remote(2, 1) == null)
                {
                    FillRepairQueues660(scheduler);
                    byte[]? batch = scheduler.NextBatch(watch.Elapsed.TotalSeconds);
                    if (batch != null)
                    {
                        events++; Check(batch.Length <= PresentationBatch.MaxSize, "original ability body keeps actual864-byte scheduler budget");
                        foreach (byte[] page in PresentationBatch.TryRead(batch, batch.Length, out var pages) ? pages! : new[] { batch })
                        {
                            if (TownServiceFragments.Stream(page, page.Length) < 0) continue;
                            byte[]? packet = fragments.Accept(2, page, page.Length, watch.Elapsed.TotalSeconds);
                            if (packet == null) continue;
                            foreach (byte[] child in TownServiceCodec.TryReadBundle(packet, packet.Length, out var children) ? children! : new[] { packet })
                                Check(TownServiceMirror.Receive(2, child, child.Length), "actual physical original survives codec and ordered receiver admission");
                        }
                    }
                    TownServiceMirror.TickRemote(_ => observer); yield return null;
                }
                TownServiceBinding? remote = Remote(2, 1);
                Check(remote != null && remote.Root.GetComponent<MeshRenderer>().enabled
                    && remote.Root.gameObject.activeInHierarchy && watch.Elapsed.TotalSeconds <= 1,
                    "actual null-prefab body is visible after real saturated transport within1s");
                Mesh received = remote!.Root.GetComponent<MeshFilter>().sharedMesh;
                Check(ReferenceEquals(local, received), "receiver uses exact source mesh despite different observer settings");
                Check(received.vertices.SequenceEqual(local.vertices) && received.triangles.SequenceEqual(local.triangles),
                    "all original body vertices and triangle partitions survive first receiver construction");
                foreach (var material in remote.Root.GetComponent<MeshRenderer>().sharedMaterials)
                    Check(material.shader == body.GetComponent<MeshRenderer>().sharedMaterials[0].shader,
                        "receiver retains original shader family after complete property validation");
                Check(Vector3.Distance(body.position, remote.Root.position) < .00002f
                    && Quaternion.Angle(body.rotation, remote.Root.rotation) < .02f
                    && Vector3.Distance(body.lossyScale, remote.Root.lossyScale) < .00002f,
                    "actual body world pose is identical before camera image isolation");
                Color32[] ownerPixels = RenderAbilityBody660(body, size, 8, key + "-owner");
                Color32[] observerPixels = RenderAbilityBody660(remote.Root, size, 9, key + "-observer");
                ComparePixels(ownerPixels, observerPixels, key);
                Check(watch.Elapsed.TotalSeconds <= 1,
                    "actual null-prefab body first rendered picture completes within1s, not merely its receipt");
                var receipts = new ExtrasSendScheduler(0, 3, 4);
                TownServiceMirror.CaptureOriginalReceipts((bytes, length) => receipts.Enqueue(bytes, length));
                FillRepairQueues660(receipts);
                byte[]? reverse = receipts.NextBatch(watch.Elapsed.TotalSeconds);
                bool acknowledged = false;
                if (reverse != null)
                    foreach (byte[] page in PresentationBatch.TryRead(reverse, reverse.Length, out var pages) ? pages! : new[] { reverse })
                        if (page.Length >= 6 && page[5] == MsgTownOriginalReceipt)
                        {
                            NetPlayerActors.Peer = 2;
                            Check(TownServiceMirror.ReceiveOriginalReceipt(10, page, page.Length),
                                "real reverse scheduler receipt credits only the exact complete body baseline");
                            acknowledged = true;
                        }
                Check(acknowledged, "actual body receiver publishes its real stored-original receipt");
                object lane = typeof(TownServiceMirror).GetField("PrivateLane", PrivateStatic)!.GetValue(null)!;
                var modules = (IDictionary)lane.GetType().GetField("Modules", PrivateInstance)!.GetValue(lane)!;
                Check((bool)typeof(TownServiceMirror).GetMethod("HasReceivedOriginal", PrivateStatic)!.Invoke(null, new[] { modules[(ushort)1] })!,
                    "exact source-object body baseline owns the real observer acknowledgment");
                NetPlayerActors.Peer = 10;
                var held = remote.Root.GetComponent<MeshFilter>();
                // The complete real contour engine upgrades original, frozen and
                // receiver consumers, with no synthetic replacement event.
                byte[] alpha = new byte[64 * 64];
                for (int y = 4; y < 60; y++) for (int x = 3; x < 61; x++)
                    if (!(x < 10 && y < 10)) alpha[y * 64 + x] = 255;
                CardMesh.SetSilhouette(CardBodyKind.Ability, alpha, 64, 64, new Rect(0, 0, 1, 1), "660 native contour fixture", fromCache: true);
                Check(ReferenceEquals(body.GetComponent<MeshFilter>().sharedMesh, held.sharedMesh),
                    "observer body follows the real later original contour completion");
                Check(ReferenceEquals(body.GetComponent<MeshFilter>().sharedMesh, parts[0].Original.GetComponent<MeshFilter>().sharedMesh),
                    "frozen original body follows the real later contour completion");
                File.AppendAllText(Path.Combine(_output, "ability-body660-costs.txt"), key + " complete=" + watch.Elapsed.TotalSeconds
                    + "s events=" + events + " vertices=" + received.vertexCount + "\n");
                TownServiceMirror.ResetNetwork(); publications.Clear();
                TownServiceMirror.ResolveTemplate = LazyTemplateProbe.Resolve;
                TownServiceMirror.PrepareInertGeometry = (a, c) => TownServiceAbilityBody.RebindClone(a, c);
            }
        }
        finally { LazyTemplateProbe.Close(); TownServiceMirror.Shutdown(); CardsDriver.CardBackingPrefab = null;
            TownServiceMirror.CollectOriginalReceiptPeers = null; NetPlayerActors.Peer = 10; }
    }

    private static void AbilityBodyBorrowOrder660()
    {
        TownServiceMirror.Shutdown(); CardsDriver.CardBackingPrefab = null;
        var sources = new Dictionary<string, TownServiceFrame>(); var sparse = new Dictionary<string, byte[]>();
        var keys = new List<string>();
        foreach (Vector2 size in new[] { new Vector2(.0635f, .088f), new Vector2(.14f, .195f) })
        {
            var owner = Go("660 real original owner dimensions").AddComponent<VRCard>();
            owner.FixtureBacking(size); keys.Add(TownServiceAbilityBody.Key(owner));
        }
        try
        {
            for (int peer = 0; peer < 2; peer++)
            {
                // Each role prepares a fresh real canonical bank and asset registry.
                // Shutdown resets actual native bases and material read caches;
                // no sender/observer hash or asset alias is handed to the other role.
                TownServiceMirror.Shutdown(); LazyTemplateProbe.Close();
                var bank = Go("660 independent role canonical bank " + peer); bank.SetActive(false);
                LazyTemplateProbe.Open(bank);
                foreach (string key in peer == 0 ? keys : keys.AsEnumerable().Reverse())
                {
                    var part = LazyTemplateProbe.Parts(key)[0];
                    Check(LazyTemplateProbe.Resolve(3, 1, key + "|"), "independent body role resolves the real frozen template");
                    using var binding = new TownServiceBinding(part.Original);
                    var actual = new TownServiceFrame { Service = 3, Session = 660, Module = 1, Sequence = 1,
                        Template = 1, TemplateAddress = key + "|", Visible = true, Structure = binding.Structure,
                        Nodes = binding.Read(TownServiceMirror.Assets, includeInactiveGraphics: true),
                        Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } };
                    if (peer == 0)
                    {
                        sources.Add(key, actual);
                        Check(TownServiceMirror.TryWriteNativeTemplateState(actual, out byte[] packet),
                            "actual body source uses a compact native basis from its independently frozen original");
                        sparse.Add(key, packet);
                    }
                    else
                    {
                        Check(TownServiceCodec.TryRead(sparse[key], sparse[key].Length, out var received)
                            && received!.NativeTemplateBasisKey != 0,
                            "body105 packet decodes with its exact sender basis identity");
                        Check(TownServiceMirror.TryExpandNativeTemplateState(received!, out var complete),
                            "independently frozen observer expands exact compact body after inverse owner-size borrow order");
                        binding.Validate(complete, TownServiceMirror.Assets);
                        for (int n = 0; n < actual.Nodes.Length; n++)
                            foreach (var property in sources[key].Nodes[n].Values)
                            {
                                Check(property.Value.Same(complete.Nodes[n].Values[property.Key]),
                                    "independent body expansion retains every exact owner property");
                                if (TownServiceFastNumbers.IsMaterial(property.Key))
                                    Check(property.Value.Same(actual.Nodes[n].Values[property.Key]),
                                        "independent body basis retains every immutable original material value");
                                else if (!property.Value.Same(actual.Nodes[n].Values[property.Key]))
                                    // Different original-bank sibling order is owner state,
                                    // transmitted explicitly by compact105's root policy.
                                    File.AppendAllText(Path.Combine(_output, "ability-body660-owner-overrides.txt"),
                                        key + " node=" + n + " property=" + property.Key + " owner="
                                        + string.Join(",", property.Value.Numbers) + " observerDefault="
                                        + string.Join(",", actual.Nodes[n].Values[property.Key].Numbers) + "\n");
                            }
                        File.AppendAllText(Path.Combine(_output, "ability-body660-bases.txt"), key + " basis="
                            + received!.NativeTemplateBasisKey.ToString("X16") + " inverse-order exact\n");
                    }
                }
            }
        }
        finally { LazyTemplateProbe.Close(); TownServiceMirror.Shutdown(); }
    }

    private static void Sender660(bool active)
    {
        object lane = typeof(TownServiceMirror).GetField("PrivateLane", PrivateStatic)!.GetValue(null)!;
        lane.GetType().GetField("Active", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(lane, active);
    }

    private static Color32[] RenderAbilityBody660(Transform body, Vector2 ownerSize, int layer, string name)
    {
        foreach (GameObject fixture in Objects) if (fixture != null) Layer(fixture.transform, 30);
        Layer(body, layer); _camera.cullingMask = 1 << layer;
        _camera.transform.SetPositionAndRotation(body.position - body.forward, body.rotation);
        _camera.orthographicSize = ownerSize.y * .65f;
        var target = new RenderTexture(512, 384, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var image = new Texture2D(512, 384, TextureFormat.RGBA32, false);
        try
        {
            _camera.targetTexture = target; _camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 512, 384), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(_output, name + ".png"), image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally
        {
            RenderTexture.active = null; _camera.targetTexture = null;
            Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        }
    }

    private static void FillRepairQueues660(ExtrasSendScheduler scheduler)
    {
        foreach (string name in new[] { "_presence", "_animation", "_plumes", "_board", "_appearance", "_prompt", "_itemAppearance", "_mapTooltip" })
        {
            var field = typeof(ExtrasSendScheduler).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            var queue = (ExtrasSendQueue)field.GetValue(scheduler)!;
            int capacity = (int)typeof(ExtrasSendQueue).GetField("_snapshotLimit", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
            byte type = (byte)typeof(ExtrasSendQueue).GetField("_payloadType", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
            byte[] bytes = new byte[capacity]; new System.Random(660).NextBytes(bytes);
            bytes[0] = 0x31; bytes[1] = 0x52; bytes[2] = 0x56; bytes[3] = 0x47; bytes[4] = 3; bytes[5] = type;
            queue.Enqueue(bytes, bytes.Length);
        }
    }
}
