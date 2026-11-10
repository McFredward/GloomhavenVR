using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Random = System.Random;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;

internal static class ResultsTransportProof
{
    private static int _assertions;
    private static void Check(bool value, string message)
    { ++_assertions; if (!value) throw new InvalidOperationException(message); }
    private static ScenarioResultsState Picture(ulong revision, float x = 0)
    {
        var node = new TownServiceNode { Binding = 1 };
        node.Values.Add(TownServiceProperty.Transform, new TownServiceValue { Numbers = new[] { x, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } });
        return new ScenarioResultsState { Key = 71, Author = 1, Revision = revision, Visible = true,
            Parts = new[] { new ScenarioResultsPart { Id = 1, Structure = 33, Address = "native.root", Nodes = new[] { node } } } };
    }
    private static byte[] Large(ScenarioResultsState state)
    {
        using var bytes = new MemoryStream(); var body = ScenarioResultsCodec.Write(state); bytes.Write(body);
        var random = new Random(761); var tail = new byte[257]; tail[0] = 250; tail[1] = 255;
        for (int row = 0; row < 20; row++)
        { for (int i = 2; i < tail.Length; i++) tail[i] = (byte)random.Next(256); bytes.Write(tail); }
        return bytes.ToArray();
    }
    public static int Main()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("GVR_RESULTS_NATIVE_ENABLED") != null) NativeStarvation();
            else { Ingress(); Emission(); SharedScrollTransport(); MotionTransport(); MotionOnlyPublisher(); MotionWorkflow(); PublisherWorkflow(); ResultsQueueLifecycle(); }
            Console.WriteLine("PASS " + _assertions + " exact Results/shared-scroll ingress/emission assertions"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Ingress()
    {
        var target = new ResultsIngress(); var basis = Picture(10); var latest = Picture(12, 12);
        var delta = ScenarioResultsDelta.Create(basis, latest)!;
        Check(target.Receive(1, ScenarioResultsCodec.Write(delta)) && target.Take(1) == null,
            "a sparse picture waits without replacing the complete visible native graph");
        Check(target.Receive(1, ScenarioResultsCodec.Write(basis)), "late exact original is admitted for dependency repair");
        var repaired = target.Take(1);
        Check(repaired?.Revision == 12 && repaired.BaseRevision == 0
            && repaired.Parts[0].Nodes[0].Values[TownServiceProperty.Transform].Numbers[0] == 12,
            "late original repairs the newest complete public picture");
        Check(target.Receive(1, ScenarioResultsCodec.Write(ScenarioResultsDelta.Create(basis, Picture(11, 11))!))
            && target.Take(1) == null, "older Results packet cannot rewind the current complete view");
        Check(target.Receive(2, ScenarioResultsCodec.Write(Picture(20, 20))) && target.Take(2) == null,
            "a non-author cannot publish native result content");
        Check(target.Receive(1, ScenarioResultsCodec.Write(Picture(13, 13))) && target.Take(1)?.Revision == 13,
            "new complete original replaces the exact previous picture");
        Check(target.Receive(1, ScenarioResultsCodec.Write(basis)) && target.Take(1) == null,
            "old full repair cannot rewind an already completed revision");
        target.Receive(1, ScenarioResultsCodec.Write(Picture(15, 15)));
        target.Receive(1, ScenarioResultsCodec.Write(Picture(16, 16)));
        Check(target.Take(1)?.Revision == 16 && target.Take(1) == null,
            "two same-frame complete updates publish only the latest coherent picture");
        var closed = Picture(17); closed.Visible = false; closed.Parts = Array.Empty<ScenarioResultsPart>();
        Check(target.Receive(1, ScenarioResultsCodec.Write(closed)) && target.Take(1)?.Visible == false,
            "native close is an ordered complete tombstone");
        var bad = ScenarioResultsCodec.Write(latest); bad[0] ^= 0x7f;
        Check(!target.Receive(1, bad) && target.Take(1) == null, "malformed input never manufactures a visible result");
    }
    private static void Emission()
    {
        var source = new FfsNetTransport(); var basis = Picture(1); byte[] full = Large(basis);
        source.Enqueue(full, basis); FfsNetTransport.NativeSocket.Attempts.Clear();
        FfsNetTransport.NativeSocket.FailNext = true; source.TickFragments(0);
        Check(source.HasFailed && FfsNetTransport.NativeSocket.Attempts.Count == 1,
            "native rejection retains the exact pending Results page");
        var delta = ScenarioResultsDelta.Create(basis, Picture(2, 2))!;
        source.Enqueue(ScenarioResultsCodec.Write(delta), delta);
        source.TickFragments(.02f);
        Check(FfsNetTransport.NativeSocket.Attempts.Count == 1, "failed admission does not cause a catch-up RPC burst");
        source.TickFragments(.051f);
        var attempts = FfsNetTransport.NativeSocket.Attempts;
        Check(attempts.Count == 2 && ReferenceEquals(attempts[0].Bytes, attempts[1].Bytes),
            "failed native Results page is retried unchanged before dependent output");
        Check(!attempts[1].Unreliable, "Results emission requests native reliable ordered delivery");
        source.TickFragments(.052f);
        Check(attempts.Count == 2, "successful retry still preserves the existing event cadence");
        for (int step = 2; step < 120; step++) source.TickFragments(step * .051f);
        var receiver = new ExtrasFragments(29, 30, ScenarioResultsCodec.MaxBytes, 180);
        var target = new ResultsIngress(); bool sawFull = false, sawDelta = false;
        for (int i = 1; i < attempts.Count; i++)
        {
            var attempt = attempts[i];
            Check(!attempt.Unreliable && attempt.Bytes.Length <= ExtrasFragments.MaxDatagramBytes,
                "every emitted Results dependency stays reliable and inside the existing datagram limit");
            byte[]? bytes = receiver.Accept(1, attempt.Bytes, attempt.Bytes.Length, i * .051);
            if (bytes == null) continue;
            Check(ScenarioResultsCodec.TryRead(bytes, bytes.Length, out var state), "native emitted pages decode with actual codec");
            sawFull |= state!.BaseRevision == 0; sawDelta |= state.BaseRevision != 0;
            Check(target.Receive(1, bytes), "actual driver ingress accepts emitted full and cumulative delta");
        }
        var current = target.Take(1);
        Check(sawFull && sawDelta && current?.Revision == 2 && current.BaseRevision == 0,
            "failed emission repair completes the exact latest native picture without a missing basis");
    }

    private static void SharedScrollTransport()
    {
        var source = new FfsNetTransport();
        var message = new SharedWindowScrollMessage { Operation = SharedScrollOperation.Wheel,
            Kind = 7, Control = 1, Epoch = 10, Opening = 20, Generation = 1,
            Sequence = 1, Author = 1, Requester = 2, Path = "/NativeScroll:0", Y = 1f };
        byte[] bytes = SharedWindowScrollCodec.Write(message);
        var attempts = FfsNetTransport.NativeSocket.Attempts;
        attempts.Clear(); FfsNetTransport.NativeSocket.HostRoutes.Clear();
        Check(source.SendSharedWindowScroll(bytes, bytes.Length, true) && attempts.Count == 1,
            "valid shared scroll input reaches the existing native control seam once");
        Check(!attempts[0].Unreliable, "shared scroll uses native reliable ordered delivery");
        Check(FfsNetTransport.NativeSocket.HostRoutes[0],
            "shared scroll requests route to the native host when requested");
        byte[] emitted = attempts[0].Bytes;
        bytes[0] ^= 0xff;
        Check(!ReferenceEquals(emitted, bytes) && SharedWindowScrollCodec.TryRead(emitted, emitted.Length, out _),
            "shared scroll emission owns immutable bytes after caller reuse");
        Check(!source.SendSharedWindowScroll(bytes, bytes.Length, true) && attempts.Count == 1,
            "malformed scroll input never reaches the native socket");
        Check(!source.SendSharedWindowScroll(emitted, emitted.Length - 1, true) && attempts.Count == 1,
            "truncated scroll input never reaches the native socket");
        byte[] results = ScenarioResultsCodec.Write(Picture(1));
        Check(!source.SendSharedWindowScroll(results, results.Length, false) && attempts.Count == 1,
            "the scroll control seam refuses a different native presentation stream");
        Check(source.SendSharedWindowScroll(emitted, emitted.Length, false)
            && !FfsNetTransport.NativeSocket.HostRoutes[1] && attempts.Count == 2,
            "common scroll state can broadcast without changing the host request route");
        Check(source.EmissionFlagsRestored, "successful control delivery restores ordinary emission flags");
        source.IsOnline = false;
        Check(!source.SendSharedWindowScroll(emitted, emitted.Length, true) && attempts.Count == 2,
            "offline shared scroll never invokes the native socket");
        source.IsOnline = true; NetSession.FlatNetMode = true;
        Check(!source.SendSharedWindowScroll(emitted, emitted.Length, true) && attempts.Count == 2,
            "flat-net mode never emits a shared scroll command");
        NetSession.FlatNetMode = false; source.SetControlAvailability(false, false);
        Check(!source.SendSharedWindowScroll(emitted, emitted.Length, true) && attempts.Count == 2,
            "uninstalled transport cannot emit a shared scroll command");
        source.SetControlAvailability(true, true);
        Check(!source.SendSharedWindowScroll(emitted, emitted.Length, true) && attempts.Count == 2,
            "degraded transport cannot emit a shared scroll command");
        source.SetControlAvailability(true, false); FfsNetTransport.NativeSocket.FailNext = true;
        Check(!source.SendSharedWindowScroll(emitted, emitted.Length, true) && attempts.Count == 3,
            "native reliable control rejection is reported without throwing into gameplay");
        Check(source.EmissionFlagsRestored, "failed control delivery restores ordinary emission flags");
    }

    private static ScenarioResultsState Motion(ulong revision, uint sequence, float x = 0)
    {
        var state = Picture(revision);
        state.HasPresentation = false; state.Parts = Array.Empty<ScenarioResultsPart>();
        state.Pose = new RigPose { Position = new Vector3(x, 0, 0), Rotation = Quaternion.identity };
        state.Move = new SharedWindowMoveEntry { Kind = 7, Phase = SharedWindowMovePhase.Held,
            ContentKey = state.Key, OpeningAuthor = 1, Opening = 1, Claim = 9, Sequence = sequence };
        return state;
    }

    private static void MotionTransport()
    {
        var source = new FfsNetTransport(); var motion = Motion(10, 10);
        byte[] bytes = ScenarioResultsCodec.Write(motion);
        var attempts = FfsNetTransport.NativeSocket.Attempts; attempts.Clear();
        Check(source.TrySendResultsMotion(bytes, bytes.Length) && attempts.Count == 1 && attempts[0].Unreliable,
            "a bounded Results motion sample bypasses reliable artwork with native unreliable delivery");
        byte[] emitted = attempts[0].Bytes; bytes[0] ^= 0xff;
        Check(!ReferenceEquals(emitted, bytes) && ScenarioResultsCodec.TryRead(emitted, emitted.Length, out _),
            "Results motion owns immutable native emission bytes");
        Check(!source.TrySendResultsMotion(bytes, bytes.Length), "Results motion validates actual bytes");
        bytes = ScenarioResultsCodec.Write(Picture(1));
        Check(!source.TrySendResultsMotion(bytes, bytes.Length), "Results motion refuses native content pictures");
        var close = Motion(11, 11); close.Visible = false; bytes = ScenarioResultsCodec.Write(close);
        Check(!source.TrySendResultsMotion(bytes, bytes.Length), "Results motion never makes close delivery unreliable");
        var oversized = new byte[865];
        Check(!source.TrySendResultsMotion(oversized, oversized.Length), "Results motion never exceeds one native datagram");
        Check(source.EmissionFlagsRestored, "Results motion restores native delivery flags");
        bytes = ScenarioResultsCodec.Write(motion); source.IsOnline = false;
        Check(!source.TrySendResultsMotion(bytes, bytes.Length), "offline Results motion is inert");
        source.IsOnline = true; NetSession.FlatNetMode = true;
        Check(!source.TrySendResultsMotion(bytes, bytes.Length), "flat-net Results motion is inert");
        NetSession.FlatNetMode = false; source.SetControlAvailability(false, false);
        Check(!source.TrySendResultsMotion(bytes, bytes.Length), "uninstalled Results motion is inert");
        source.SetControlAvailability(true, true);
        Check(!source.TrySendResultsMotion(bytes, bytes.Length), "degraded Results motion is inert");
        source.SetControlAvailability(true, false); FfsNetTransport.NativeSocket.FailNext = true;
        Check(!source.TrySendResultsMotion(bytes, bytes.Length) && source.EmissionFlagsRestored,
            "failed Results motion neither throws nor changes the next packet delivery flags");

        var target = new ResultsIngress(); var basis = Picture(20); var future = Motion(24, 24);
        target.Receive(1, ScenarioResultsCodec.Write(basis)); target.Receive(1, ScenarioResultsCodec.Write(future));
        Check(target.Take(1)?.Revision == 20 && target.Motion(1)?.Revision == 24,
            "live motion cannot erase an unapplied complete native picture");
        var delta = ScenarioResultsDelta.Create(basis, Picture(22, 22))!;
        target.Receive(1, ScenarioResultsCodec.Write(delta));
        Check(target.Take(1)?.Revision == 22 && target.Motion(1)?.Revision == 24,
            "a newer live motion watermark cannot discard an older exact sparse content dependency");
        target.Receive(1, ScenarioResultsCodec.Write(Motion(23, 23)));
        Check(target.Motion(1)?.Revision == 24, "out-of-order motion cannot rewind the newest live target");
    }

    private static string Trace(double at, int sender, ScenarioResultsState state)
    {
        SharedWindowMoveEntry move = state.Move!.Value;
        object[] row = { at, sender, state.Revision, state.HasPresentation ? 1 : 0, state.Visible ? 1 : 0,
            state.Key, state.Author, state.SampleTime, state.Pose.Position.x, state.Pose.Position.y,
            state.Pose.Position.z, state.Pose.Rotation.x, state.Pose.Rotation.y, state.Pose.Rotation.z,
            state.Pose.Rotation.w, state.Size, state.Reference, state.Origin.x, state.Origin.y,
            state.Origin.z, state.Rotation.x, state.Rotation.y, state.Rotation.z, state.Rotation.w,
            state.Scale, move.Kind, (byte)move.Phase, move.ContentKey, move.OpeningAuthor,
            move.Opening, move.Claim, move.Sequence, move.ParentClaim, move.ParentAuthor };
        return string.Join("\t", Array.ConvertAll(row, value => value is IFormattable number
            ? number.ToString(null, CultureInfo.InvariantCulture) : value.ToString()));
    }

    private static void MotionOnlyPublisher()
    {
        var publisher = new ResultsIngress(); var attempts = FfsNetTransport.NativeSocket.Attempts; attempts.Clear();
        for (int sample = 0; sample < 40; sample++)
        {
            var state = Picture(1); var live = Motion(1, (uint)sample + 1, sample);
            state.Pose = live.Pose; state.Move = live.Move;
            publisher.Publish(state, sample * .07f); publisher.Transport.TickFragments(sample * .07f);
        }
        int full = 0, motion = 0;
        foreach (var packet in attempts)
            if (ScenarioResultsCodec.TryRead(packet.Bytes, packet.Bytes.Length, out var state) && !state!.HasPresentation) ++motion;
            else if (PresentationBatch.HasResultsPage(packet.Bytes, packet.Bytes.Length)) ++full;
        Check(motion == 40 && full <= 6,
            "a moving static Results picture serializes only its bounded content refreshes and independent tiny poses");
    }

    private static void MotionWorkflow()
    {
        var publisher = new ResultsIngress(); var observer = new ResultsIngress();
        var receiver = new FfsNetTransport { LocalPlayerId = 2 }; var trace = new List<string>();
        double delivery = 0; int motionCount = 0, fullCount = 0, dropped = 0;
        float lastMotionTime = 0, worstGap = 0;
        receiver.PacketReceived += (sender, bytes, length) =>
        {
            Check(observer.Receive(sender, bytes), "motion workflow executes actual Results ingress");
            var complete = observer.Take(sender);
            if (complete != null) { ++fullCount; trace.Add(Trace(delivery, sender, complete)); }
            var current = observer.Motion(sender);
            if (current != null && current.SampleTime > lastMotionTime)
            {
                if (lastMotionTime >= .5f) worstGap = Math.Max(worstGap, current.SampleTime - lastMotionTime);
                lastMotionTime = current.SampleTime; ++motionCount;
                trace.Add(Trace(delivery, sender, current));
            }
        };
        var pending = new List<(double At, byte[] Bytes)>();
        var attempts = FfsNetTransport.NativeSocket.Attempts; attempts.Clear();
        int delivered = 0, emittedMotion = 0;
        for (int frame = 0; frame <= 450; frame++)
        {
            float now = frame / 90f;
            ScenarioResultsState state = now < .5f ? Picture(1) : Stats(now);
            var live = Motion(1, (uint)frame + 1, now < .5f ? 0 : (now - .5f) * 2f);
            state.Move = live.Move; state.Pose = live.Pose;
            publisher.Publish(state, now); publisher.Transport.TickFragments(now);
            while (delivered < attempts.Count)
            {
                var packet = attempts[delivered++];
                bool tiny = ScenarioResultsCodec.TryRead(packet.Bytes, packet.Bytes.Length, out var sample)
                    && !sample!.HasPresentation;
                if (tiny)
                {
                    Check(packet.Unreliable && packet.Bytes.Length <= 864,
                        "each live pose is independent of the reliable incomplete native graph");
                    ++emittedMotion;
                    if (emittedMotion % 13 == 0) { ++dropped; continue; }
                }
                else Check(!packet.Unreliable, "motion does not weaken complete native picture reliability");
                double jitter = tiny && emittedMotion % 11 == 0 ? .11 : (frame % 3) * .006;
                pending.Add((now + .02 + jitter, packet.Bytes));
            }
            pending.Sort((a, b) => a.At.CompareTo(b.At));
            while (pending.Count > 0 && pending[0].At <= now)
            {
                var packet = pending[0]; pending.RemoveAt(0); delivery = now;
                receiver.Dispatch(1, packet.Bytes);
            }
        }
        Console.WriteLine($"MOTION emitted={emittedMotion}, received={motionCount}, full={fullCount}, worst source gap={worstGap:F3}s");
        Check(motionCount >= 45 && emittedMotion <= 76 && dropped > 0 && worstGap < .24f,
            "actual producer and receiver retain bounded fifteen-Hz motion despite loss, jitter and a fragmented native graph");
        Check(fullCount > 1, "independent live movement leaves complete native picture progress intact");
        string? output = Environment.GetEnvironmentVariable("GVR_RESULTS_MOTION_TRACE_OUT");
        if (output != null) File.WriteAllLines(output, trace);
        // A stalled source never sends a burst of historical poses on its next frame.
        int before = attempts.Count; publisher.Publish(Stats(), 20);
        Check(attempts.Count <= before + 1, "a stalled Results publisher emits at most one current live pose");
        Console.WriteLine($"MOTION emitted={emittedMotion}, received={motionCount}, full={fullCount}, worst source gap={worstGap:F3}s, trace={output}");
    }

    private static ScenarioResultsState Stats(float x = 0)
    {
        ScenarioResultsState picture = Picture(1, x);
        var nodes = new TownServiceNode[96]; var random = new Random(769);
        for (int row = 0; row < nodes.Length; row++)
        {
            var text = new char[320];
            for (int at = 0; at < text.Length; at++) text[at] = (char)random.Next(33, 127);
            var node = new TownServiceNode { Binding = (uint)row + 2 };
            node.Values.Add(TownServiceProperty.TmpText, new TownServiceValue { Text = new[] { new string(text) } });
            nodes[row] = node;
        }
        picture.Parts = new[] { picture.Parts[0], new ScenarioResultsPart { Id = 2, ParentId = 1,
            ParentBinding = 1, Structure = 769, Address = "native.stats", Nodes = nodes } };
        return picture;
    }

    private static byte[] Traffic(byte type)
    {
        // Independently occupied public streams use opaque future-TLV contents.
        // Actual scheduler admission, packing and capacity remain in production.
        var random = new Random(type);
        int rows = type == NetProtocol.MsgNativeUseBar ? 7 : 12;
        var bytes = new byte[6 + rows * 257];
        bytes[0] = 0x31; bytes[1] = 0x52; bytes[2] = 0x56; bytes[3] = 0x47;
        bytes[4] = 3; bytes[5] = type;
        for (int at = 6; at < bytes.Length; at += 257)
        { bytes[at] = 250; bytes[at + 1] = 255; for (int n = 2; n < 257; n++) bytes[at + n] = (byte)random.Next(256); }
        return bytes;
    }

    private sealed class Client
    {
        private readonly FfsNetTransport _transport = new() { LocalPlayerId = 2 };
        private readonly ResultsIngress _driver = new();
        private readonly List<byte[]> _delayed = new();
        internal ScenarioResultsState? Last;
        internal int Full, Delta, PartsSeen;
        private ulong _sequence;
        internal Client() { _transport.PacketReceived += (sender, bytes, length) => Complete(bytes); }
        internal void Receive(byte[] page, double now, bool reverse)
        {
            if (!reverse) { _transport.Dispatch(1, page); return; }
            int type = NetPacket.PeekType(page, page.Length);
            if (type == NetProtocol.MsgPresentationBatch)
            {
                Check(PresentationBatch.TryRead(page, page.Length, out var children), "contention batch decodes");
                foreach (byte[] child in children!) Receive(child, now, reverse);
                return;
            }
            if (type != 30 && !(type == NetProtocol.MsgPresentationCompression
                && ExtrasFragments.CompressedPayloadType(page, page.Length) == 29)) return;
            ulong sequence = 0; for (int b = 0; b < 8; b++) sequence |= (ulong)page[8 + b] << (8 * b);
            if (reverse)
            {
                if (_sequence != sequence) { Flush(now); _sequence = sequence; }
                _delayed.Add(page); return;
            }
            Apply(page, now);
        }
        internal void Flush(double now)
        { for (int i = _delayed.Count - 1; i >= 0; i--) Apply(_delayed[i], now); _delayed.Clear(); }
        private void Apply(byte[] page, double now)
        { _transport.Dispatch(1, page); }
        private void Complete(byte[] full)
        {
            if (NetPacket.PeekType(full, full.Length) != 29) return;
            Check(ScenarioResultsCodec.TryRead(full, full.Length, out var wire), "workflow reassembly decodes original Results125");
            if (wire!.BaseRevision == 0) Full++; else Delta++;
            Check(_driver.Receive(1, full), "workflow passes actual Results ingress");
            Last = _driver.Take(1) ?? Last; PartsSeen = Math.Max(PartsSeen, Last?.Parts.Length ?? 0);
        }
    }

    private static void PublisherWorkflow()
    {
        var publisher = new ResultsIngress(); var live = new Client(); var reordered = new Client();
        var attempts = FfsNetTransport.NativeSocket.Attempts; attempts.Clear();
        var heading = Picture(1); publisher.Publish(heading, 0);
        int delivered = 0;
        for (int step = 0; step < 8; step++)
        {
            float now = step * .051f; publisher.Transport.TickFragments(now);
            while (delivered < attempts.Count)
            { var item = attempts[delivered++]; live.Receive(item.Bytes, now, false); reordered.Receive(item.Bytes, now, true); }
        }
        reordered.Flush(.4);
        Check(live.Last?.Parts.Length == 1 && reordered.Last?.Parts.Length == 1,
            "two distinct clients first display the heading-only full basis");
        var stats = Stats(); Check(ScenarioResultsCodec.Write(stats).Length > 20000,
            "structural Stats basis requires many real fragments");
        Check(!ScenarioResultsDelta.Compatible(heading, stats), "Stats structure requires a new full original");
        byte[] presence = Traffic(NetProtocol.MsgExtras), animation = Traffic(NetProtocol.MsgUseBarAnimation),
            board = Traffic(NetProtocol.MsgNativeBoard), appearance = Traffic(NetProtocol.MsgCardAppearance),
            itemAppearance = Traffic(NetProtocol.MsgItemAppearance), tooltip = Traffic(NetProtocol.MsgMapButtonTooltip),
            prompt = Traffic(NetProtocol.MsgNativeDecisionPrompt), plume = Traffic(NetProtocol.MsgCardPlume),
            native = Traffic(NetProtocol.MsgNativeUseBar);
        int resultPages = 0, backgroundPages = 0;
        for (int step = 8; step < 1800; step++)
        {
            float now = step * .051f;
            // Repeated changing samples and refresh heartbeats are authored by
            // the real publisher while its admitted large basis is in flight.
            publisher.Publish(Stats(step < 50 ? step : 50), now);
            publisher.Transport.EnqueueBackground(presence); publisher.Transport.EnqueueBackground(animation);
            publisher.Transport.EnqueueBackground(board); publisher.Transport.EnqueueBackground(appearance);
            publisher.Transport.EnqueueBackground(itemAppearance); publisher.Transport.EnqueueBackground(tooltip);
            publisher.Transport.EnqueueBackground(prompt); publisher.Transport.EnqueueBackground(plume);
            for (int slot = 8; slot < 32; slot++) publisher.Transport.EnqueueBackground(native, slot);
            publisher.Transport.TickFragments(now);
            while (delivered < attempts.Count)
            {
                var packet = attempts[delivered++];
                Check(packet.Bytes.Length <= 864, "contention preserves existing event byte cap");
                bool result = PresentationBatch.HasResultsPage(packet.Bytes, packet.Bytes.Length);
                if (result) { resultPages++; Check(!packet.Unreliable, "every workflow Results dependency is reliable ordered"); }
                else backgroundPages++;
                live.Receive(packet.Bytes, now, false); reordered.Receive(packet.Bytes, now, true);
            }
        }
        reordered.Flush(92);
        foreach (var client in new[] { live, reordered })
        {
            Check(client.Full >= 2 && client.Delta > 0 && client.PartsSeen == 2 && client.Last?.Parts.Length == 2,
                "heading-to-Stats full baseline and cumulative updates survive concurrent public streams");
            Check(client.Last!.Parts[0].Nodes[0].Values[TownServiceProperty.Transform].Numbers[0] == 50,
                "both clients converge to latest Stats sample after coalescing and same-snapshot page reordering");
        }
        Check(resultPages > 30 && backgroundPages > 30, "workflow exercises fragmented Results and sustained competing output");
        Console.WriteLine($"WORKFLOW result events={resultPages}, background events={backgroundPages}, live full/delta={live.Full}/{live.Delta}, reordered full/delta={reordered.Full}/{reordered.Delta}");
    }

    private static void NativeStarvation()
    {
        byte[] onBytes = File.ReadAllBytes(Environment.GetEnvironmentVariable("GVR_RESULTS_NATIVE_ENABLED")!);
        byte[] offBytes = File.ReadAllBytes(Environment.GetEnvironmentVariable("GVR_RESULTS_NATIVE_DISABLED")!);
        Check(ScenarioResultsCodec.TryRead(onBytes, onBytes.Length, out var on)
            && ScenarioResultsCodec.TryRead(offBytes, offBytes.Length, out _), "actual native capture samples decode");
        ScenarioResultsCodec.TryRead(offBytes, offBytes.Length, out var off);
        if (on == null || off == null) throw new InvalidOperationException("Native capture sample decoding failed.");
        Check(on.Parts.Length > 1 && off.Parts.Length == on.Parts.Length,
            "actual native Stats samples retain their complete module census");
        Check(on.Key == off.Key && on.Author == 1 && off.Author == 1,
            "actual native samples share one author opening");
        Check(!ScenarioResultsDelta.Compatible(on, off),
            "actual Graphic enabled transition changes complete-property membership");
        int pages = ExtrasFragments.Encode(onBytes, onBytes.Length, 1, 29, 30, ScenarioResultsCodec.MaxBytes, true).Length;
        Check(pages > 1, "actual native Stats full basis requires multiple transport events");
        var publisher = new ResultsIngress(); var client = new Client();
        var attempts = FfsNetTransport.NativeSocket.Attempts; attempts.Clear(); int delivered = 0;
        var heading = Picture(1); heading.Key = on.Key;
        for (int step = 0; step < 8; step++)
        {
            float now = step * .051f; publisher.Publish(heading, now); publisher.Transport.TickFragments(now);
            while (delivered < attempts.Count) client.Receive(attempts[delivered++].Bytes, now, false);
        }
        Check(client.Last?.Parts.Length == 1, "native workflow begins from an already completed heading");
        int firstStats = -1;
        // Each source sample is a frozen output of the actual Unity sampler.
        // Alternate one native Graphic enable transition, with no wire mutation.
        for (int step = 8; step < 800; step++)
        {
            float now = step * .051f; publisher.Publish((step / 2) % 2 == 0 ? on : off, now);
            publisher.Transport.TickFragments(now);
            while (delivered < attempts.Count)
            {
                var packet = attempts[delivered++];
                bool motion = ScenarioResultsCodec.TryRead(packet.Bytes, packet.Bytes.Length, out var decoded) && !decoded!.HasPresentation;
                Check(packet.Bytes.Length <= 864 && (motion ? packet.Unreliable : !packet.Unreliable),
                    "native sampled Stats retains reliable bounded emission");
                client.Receive(packet.Bytes, now, false);
            }
            if (client.Last?.Parts.Length == on.Parts.Length && firstStats < 0) firstStats = step;
        }
        Console.WriteLine($"NATIVE STARVATION bytes={onBytes.Length}/{offBytes.Length}, pages={pages}, emitted={attempts.Count}, completed={client.Full}, first Stats step={firstStats}");
        Check(firstStats >= 0 && client.Last?.Parts.Length == on.Parts.Length,
            "native sampled Stats must eventually replace the completed heading");
        ResultsQueueLifecycle();
    }

    private static void ResultsQueueLifecycle()
    {
        static ExtrasSendQueue Queue() => new(100, 29, 30, true, ScenarioResultsCodec.MaxBytes);
        static int Waiting(ExtrasSendQueue queue)
        {
            object pending = typeof(ExtrasSendQueue).GetField("_pending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(queue)!;
            return ((System.Collections.ICollection)pending).Count;
        }
        var queue = Queue(); var first = Picture(1); byte[] firstBytes = Large(first);
        queue.EnqueueResults(firstBytes, firstBytes.Length, first);
        var receiver = new ExtrasFragments(29, 30, ScenarioResultsCodec.MaxBytes, 180);
        byte[] page = queue.Next(0)!; receiver.Accept(1, page, page.Length, 0);
        Check(queue.HasInFlight, "lifecycle begins inside a multi-page complete basis");
        for (ulong revision = 2; revision <= 20; revision++)
        {
            var basis = Picture(revision, revision); byte[] bytes = Large(basis);
            queue.EnqueueResults(bytes, bytes.Length, basis);
            Check(Waiting(queue) == 1, "newer full originals coalesce to one bounded waiter");
        }
        for (ulong revision = 22; revision <= 60; revision += 2)
        {
            var basis = Picture(revision, revision); byte[] bytes = Large(basis);
            queue.EnqueueResults(bytes, bytes.Length, basis);
            var delta = ScenarioResultsDelta.Create(basis, Picture(revision + 1, revision + 1))!;
            bytes = ScenarioResultsCodec.Write(delta); queue.EnqueueResults(bytes, bytes.Length, delta);
            Check(Waiting(queue) == 2, "only the latest waiting full and its named cumulative delta remain");
            bytes = Large(basis); queue.EnqueueResults(bytes, bytes.Length, basis);
            Check(Waiting(queue) == 2, "duplicate waiting full cannot discard its newer named delta");
        }
        var driver = new ResultsIngress(); var completed = new List<ulong>();
        for (int step = 1; step < 60; step++)
        {
            page = queue.Next(step * .051)!; if (page == null) continue;
            byte[]? complete = receiver.Accept(1, page, page.Length, step * .051);
            if (complete == null) continue;
            Check(ScenarioResultsCodec.TryRead(complete, complete.Length, out var state), "lifecycle assembled basis is valid");
            completed.Add(state!.Revision); driver.Receive(1, complete);
        }
        var latest = driver.Take(1);
        Check(completed.Count == 3 && completed[0] == 1 && completed[1] == 60 && completed[2] == 61,
            "stale active full finishes once before the latest full and its dependency-preserving delta");
        Check(latest?.Revision == 61 && latest.Parts[0].Nodes[0].Values[TownServiceProperty.Transform].Numbers[0] == 61,
            "finishing an older basis cannot rewind the final coalesced picture");
        Check(Waiting(queue) == 0 && !queue.HasInFlight, "completed lifecycle leaves no growing Results backlog");

        foreach (bool changeAuthor in new[] { false, true })
        {
            queue = Queue(); queue.EnqueueResults(firstBytes, firstBytes.Length, first); queue.Next(0);
            var next = Picture(1); if (changeAuthor) next.Author = 3; else next.Key = 72;
            byte[] bytes = ScenarioResultsCodec.Write(next); queue.EnqueueResults(bytes, bytes.Length, next);
            receiver = new ExtrasFragments(29, 30, ScenarioResultsCodec.MaxBytes, 180);
            page = queue.Next(.051)!; byte[]? complete = receiver.Accept(1, page, page.Length, .051);
            Check(complete != null && ScenarioResultsCodec.TryRead(complete, complete.Length, out var state)
                && state!.Key == next.Key && state.Author == next.Author,
                "new key or author immediately supersedes the old in-flight full");
            Check(queue.Next(.102) == null, "new opening leaves no obsolete old-opening tail");
        }
        queue = Queue(); queue.EnqueueResults(firstBytes, firstBytes.Length, first); queue.Next(0);
        var close = Picture(2); close.Visible = false; close.Parts = Array.Empty<ScenarioResultsPart>();
        byte[] closed = ScenarioResultsCodec.Write(close); queue.EnqueueResults(closed, closed.Length, close);
        receiver = new ExtrasFragments(29, 30, ScenarioResultsCodec.MaxBytes, 180); page = queue.Next(.051)!;
        byte[]? tombstone = receiver.Accept(1, page, page.Length, .051);
        Check(tombstone != null && ScenarioResultsCodec.TryRead(tombstone, tombstone.Length, out var closure)
            && !closure!.Visible, "native close immediately supersedes the old in-flight full");
        Check(queue.Next(.102) == null, "close leaves no obsolete visible full or sparse tail");
    }
}
