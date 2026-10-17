using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using GloomhavenVR.Core.SteamLink;

internal static class PresenterSourceControls
{
    private const string Session = "0123456789abcdef0123456789abcdef";
    private static int _checks, _cases, _causalMutations;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception("Presenter source: " + message);
    }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, message);
    }
    private static int[] Integers(JsonElement element) => element.EnumerateArray().Select(x => x.GetInt32()).ToArray();
    // Independent test oracle reads the frozen protocol, never production Codes.
    private static int Cell(int x, int y, JsonElement guard)
    {
        int originX = guard.GetProperty("origin")[0].GetInt32(), originY = guard.GetProperty("origin")[1].GetInt32();
        int width = guard.GetProperty("size")[0].GetInt32(), height = guard.GetProperty("size")[1].GetInt32();
        int step = guard.GetProperty("stride")[0].GetInt32();
        if (y < originY || y >= originY + height || x < originX) return -1;
        int cell = (x - originX) / step;
        return cell < guard.GetProperty("cells").GetInt32() && (x - originX) % step < width ? cell : -1;
    }
    private static bool Recognizes(byte[] canvas, JsonElement guard)
    {
        int[] origin = Integers(guard.GetProperty("origin")), size = Integers(guard.GetProperty("size"));
        int[] codes = Integers(guard.GetProperty("codes"));
        int stride = guard.GetProperty("stride")[0].GetInt32(), tolerance = guard.GetProperty("code_tolerance").GetInt32();
        for (int cell = 0; cell < codes.Length; cell++)
        {
            int x = origin[0] + stride * cell + size[0] / 2, y = origin[1] + size[1] / 2;
            int at = (y * 688 + x) * 3;
            if (Math.Abs(canvas[at + 1] - codes[cell]) > tolerance) return false;
        }
        return true;
    }
    private static void VerifyCase(ulong pair, ProbeEye eye, JsonElement spec, string output)
    {
        var context = RegistrationSource.ExpectedContext(Session, pair, 17 + (long)pair);
        var pattern = RegistrationSource.PreparePattern(Session, eye, context.Phase);
        var old = RegistrationSource.CreateBuffers(); var actual = RegistrationSource.CreateBuffers();
        RegistrationSourceRecord oldRecord = RegistrationSource.Stamp(pattern, context, old);
        RegistrationSourceRecord record = PresenterSource.Stamp(pattern, context, actual);
        JsonElement guard = spec.GetProperty("guard"); int[] codes = Integers(guard.GetProperty("codes"));
        Check(old.Color.SequenceEqual(actual.Color) && old.Coverage.SequenceEqual(actual.Coverage), "logical color and numeric coverage unchanged");
        Check(old.Header.SequenceEqual(actual.Header), "all original CRC header bytes unchanged");
        int alteredPixels = 0, paintedPixels = 0, outsideDifferences = 0, wrongGuardBytes = 0;
        // Compute intervals once, keeping the independent complete-byte oracle cheap.
        int[] wantedCell = Enumerable.Range(0, 688 * 548).Select(i => Cell(i % 688, i / 688, guard)).ToArray();
        for (int pixel = 0; pixel < wantedCell.Length; pixel++)
        {
            int cell = wantedCell[pixel], at = pixel * 3;
            if (cell >= 0) paintedPixels++;
            bool differs = false;
            for (int channel = 0; channel < 3; channel++)
            {
                if (old.Canvas[at + channel] != actual.Canvas[at + channel])
                {
                    differs = true;
                    if (cell < 0) outsideDifferences++;
                }
                if (cell >= 0 && actual.Canvas[at + channel] != codes[cell]) wrongGuardBytes++;
            }
            if (differs) alteredPixels++;
        }
        Check(outsideDifferences == 0, "EVERY byte outside new guard, including all headers/markers/planes, stays identical");
        Check(paintedPixels == 12 * 32 * 32 && wrongGuardBytes == 0 && alteredPixels > 0, "exact complete neutral-gray guard rectangles");
        Check(Recognizes(actual.Canvas, guard) && !Recognizes(old.Canvas, guard), "new profile recognized and old v1 refused");
        Check(record.Profile == spec.GetProperty("profile").GetString() && record.ProfileRevision == 2 && oldRecord.ProfileRevision == 1
            && oldRecord.Profile == RegistrationSource.Profile, "explicit new wrapper profile without changing old wrapper");
        Check(record.Manifest.Convention == "straight" && record.Manifest.LayoutRevision == oldRecord.Manifest.LayoutRevision
            && record.Manifest.ColorSha256 == oldRecord.Manifest.ColorSha256
            && record.Manifest.CoverageSha256 == oldRecord.Manifest.CoverageSha256, "same full-header/panel conventions and identities");
        Check(record.Manifest.CanvasSha256 == ProbeManifest.Sha256(actual.Canvas)
            && record.Manifest.CanvasSha256 != oldRecord.Manifest.CanvasSha256, "new whole-canvas hash is recomputed");
        Check(record.Session == oldRecord.Session && record.PairId == pair && record.Eye == oldRecord.Eye
            && record.EngineFrameCohort == context.EngineFrameCohort, "frozen stereo source tuple preserved");
        Check(!record.PassthroughAuthorized && !record.SourceFrameAssociationProven && !record.GpuCompletionProven
            && !record.PhysicalEyeProven && record.OpaqueFallbackRequired, "no new authority inferred from a guard");
        record.Manifest.Check(record.Eye);
        using (JsonDocument json = JsonDocument.Parse(record.ToJsonBytes()))
            Check(json.RootElement.GetProperty("profile").GetString() == PresenterSource.Profile
                && json.RootElement.GetProperty("profile_revision").GetInt32() == 2
                && json.RootElement.GetProperty("passthrough_authorized").GetBoolean() == false, "serialized actual-profile receipt");
        // A v2 call must not mutate its reusable underlying v1 pattern workspace.
        RegistrationSourceRecord again = RegistrationSource.Stamp(pattern, context, actual);
        Check(actual.Canvas.SequenceEqual(old.Canvas) && again.Manifest.CanvasSha256 == oldRecord.Manifest.CanvasSha256
            && again.Profile == RegistrationSource.Profile, "v1 after v2 restores old bytes and old identity exactly");
        if (pair == 1 && eye == ProbeEye.Left)
        {
            record = PresenterSource.Stamp(pattern, context, actual);
            File.WriteAllBytes(Path.Combine(output, "presenter-left.rgb"), actual.Canvas);
            File.WriteAllBytes(Path.Combine(output, "presenter-left.color"), actual.Color);
            File.WriteAllBytes(Path.Combine(output, "presenter-left.coverage"), actual.Coverage);
            File.WriteAllBytes(Path.Combine(output, "presenter-left.json"), record.ToJsonBytes());
            byte[] mutated = (byte[])actual.Canvas.Clone();
            for (int cell = 0; cell < codes.Length; cell++)
            {
                int at = ((464 + 16) * 688 + 16 + 52 * cell + 16) * 3 + 1;
                byte saved = mutated[at];
                foreach (int delta in new[] { -2, 2 }) { mutated[at] = (byte)(saved + delta); Check(Recognizes(mutated, guard), "declared tolerance endpoint"); }
                foreach (int delta in new[] { -3, 3 }) { mutated[at] = (byte)(saved + delta); Check(!Recognizes(mutated, guard), "each single-cell causal mismatch refuses"); _causalMutations++; }
                mutated[at] = saved;
            }
            Check(!Recognizes(new byte[actual.Canvas.Length], guard), "ordinary zero image is not a presenter source");
            mutated[0] ^= 128;
            Check(Recognizes(mutated, guard), "explicit limitation: intact guard cannot authenticate a changed body");
        }
        _cases++;
    }
    public static int Main(string[] arguments)
    {
        string output = arguments[1]; Directory.CreateDirectory(output);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(arguments[0]));
        JsonElement spec = document.RootElement, guard = spec.GetProperty("guard");
        Check(PresenterSource.Profile == spec.GetProperty("profile").GetString() && PresenterSource.ProfileRevision == 2,
            "profile/revision match frozen shared protocol");
        string preimage = guard.GetProperty("signature_preimage").GetString()!;
        string digest = ProbeManifest.Sha256(Encoding.ASCII.GetBytes(preimage));
        Check(PresenterSource.SignaturePreimage == preimage && PresenterSource.SignatureSha256 == digest
            && digest == guard.GetProperty("signature_sha256").GetString(), "signature preimage and SHA256 match independent protocol");
        Check(Integers(guard.GetProperty("codes")).SequenceEqual(Convert.FromHexString(digest).Take(12).Select(x => (int)x)), "12-byte signature derives from fixed preimage");
        foreach (ulong pair in new ulong[] { 1, 2, 65, 128 })
            foreach (ProbeEye eye in new[] { ProbeEye.Left, ProbeEye.Right }) VerifyCase(pair, eye, spec, output);
        var buffers = RegistrationSource.CreateBuffers();
        var context = RegistrationSource.ExpectedContext(Session, 2, 99);
        var wrong = RegistrationSource.PreparePattern(Session, ProbeEye.Left, 0);
        Reject(() => PresenterSource.Stamp(wrong, context, buffers), "underlying wrong-phase refusal preserved");
        Reject(() => PresenterSource.Stamp(null!, context, buffers), "underlying missing-pattern refusal preserved");
        Console.WriteLine(JsonSerializer.Serialize(new { checks = _checks, cases = _cases, causal_mutations = _causalMutations,
            profile = PresenterSource.Profile, profile_revision = 2, source_sha256 = ProbeManifest.Sha256(File.ReadAllBytes(Path.Combine(output, "presenter-left.rgb"))),
            scope = "pure CPU controlled difference and source metadata; no GPU or XR authority", passthrough_authorized = false }));
        return 0;
    }
}
