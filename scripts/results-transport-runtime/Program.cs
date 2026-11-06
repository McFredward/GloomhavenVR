using System;
using System.IO;
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
        try { Ingress(); Emission(); Console.WriteLine("PASS " + _assertions + " exact Results ingress/emission assertions"); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
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
}
