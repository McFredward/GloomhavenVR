using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GloomhavenVR.Core.SteamLink;

internal static class RegistrationSourceControls
{
    private const string Session = "0123456789abcdef0123456789abcdef";
    private static int _checks;
    private static void Require(bool value, string message)
    {
        _checks++;
        if (!value) throw new Exception("Control failed: " + message);
    }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Require(rejected, message);
    }
    private static RegistrationPairPolicy Policy(ulong previous = 0) => new RegistrationPairPolicy(Guid.ParseExact(Session, "N"), previous);
    private static RegistrationPairContext Prepare(RegistrationPairPolicy policy, long cohort)
    {
        Require(policy.TryPreparePair(cohort, out RegistrationPairContext? context, out RegistrationPairRefusal reason)
                && context != null && reason == RegistrationPairRefusal.None, "preparation reserves one frozen context");
        return context!;
    }
    private static void Eye(RegistrationPairPolicy policy, RegistrationPairContext context, ProbeEye eye)
    {
        Require(policy.TryBeginEye(context, context.EngineFrameCohort, eye, out RegistrationPairRefusal reason)
                && reason == RegistrationPairRefusal.None, "real distinct eye starts");
        Require(policy.CompleteEye(context, eye, out reason) && reason == RegistrationPairRefusal.None, "recorded eye commits");
    }

    private static void PolicyControls()
    {
        var sessions = new HashSet<string>();
        for (int i = 0; i < 32; i++)
        {
            var fresh = new RegistrationPairPolicy();
            Require(RegistrationSource.IsSession(fresh.Session) && sessions.Add(fresh.Session), "new runs get fresh nonzero128-bit sessions");
        }
        Reject(() => new RegistrationPairPolicy(Guid.Empty), "empty nonce refused");
        var run = Policy();
        var first = Prepare(run, 17);
        Require(first.PairId == 1 && first.EngineFrameCohort == 17 && first.Phase == 0, "app pair starts1 independently of engine cohort17");
        Require(!run.IsPairComplete(first), "reservation is not an output");
        Eye(run, first, ProbeEye.Right);
        Require(!run.IsPairComplete(first), "one eye cannot complete a pair");
        Eye(run, first, ProbeEye.Left);
        Require(run.IsPairComplete(first) && run.Pending == null, "two distinct recorded callbacks complete only this CPU pair");
        var second = Prepare(run, 900);
        Require(second.PairId == 2 && second.Phase == 1 && first.PairId == 1, "frozen earlier context and consecutive owned IDs despite cohort gap");
        Eye(run, second, ProbeEye.Left); Eye(run, second, ProbeEye.Right);
        Require(!run.IsPairComplete(first) && run.IsPairComplete(second), "completion receipt never joins against arbitrary latest context");
        Require(!run.TryPreparePair(900, out _, out RegistrationPairRefusal reason) && reason == RegistrationPairRefusal.StaleCohort,
                "completed cohort cannot be reused");

        for (int fault = 0; fault < 9; fault++)
        {
            var policy = Policy(); var context = Prepare(policy, 12);
            bool accepted = true;
            RegistrationPairRefusal expected;
            switch (fault)
            {
                case 0: accepted = policy.TryPreparePair(13, out _, out reason); expected = RegistrationPairRefusal.IncompletePair; break;
                case 1: accepted = policy.TryBeginEye(context, 11, ProbeEye.Left, out reason); expected = RegistrationPairRefusal.StaleCohort; break;
                case 2: accepted = policy.TryBeginEye(context, 12, ProbeEye.Unknown, out reason); expected = RegistrationPairRefusal.UnknownEye; break;
                case 3:
                    policy.TryBeginEye(context, 12, ProbeEye.Left, out _);
                    accepted = policy.TryBeginEye(context, 12, ProbeEye.Left, out reason); expected = RegistrationPairRefusal.DuplicateEye; break;
                case 4: accepted = policy.CompleteEye(context, ProbeEye.Left, out reason); expected = RegistrationPairRefusal.EyeNotBegun; break;
                case 5:
                    Eye(policy, context, ProbeEye.Left);
                    accepted = policy.CompleteEye(context, ProbeEye.Left, out reason); expected = RegistrationPairRefusal.AlreadyCompleted; break;
                case 6:
                    policy.TryBeginEye(context, 12, ProbeEye.Left, out _);
                    accepted = policy.DiscardPrepared(context, out reason); expected = RegistrationPairRefusal.PairAlreadyStarted; break;
                case 7:
                    var clone = RegistrationSource.ExpectedContext(context.Session, context.PairId, 12);
                    accepted = policy.TryBeginEye(clone, 12, ProbeEye.Left, out reason); expected = RegistrationPairRefusal.ForeignContext; break;
                default: accepted = policy.CompleteEye(context, (ProbeEye)999, out reason); expected = RegistrationPairRefusal.UnknownEye; break;
            }
            Require(!accepted && reason == expected && policy.IsFaulted && policy.Pending == null, "causal invalid callback/cohort fails closed");
            Require(policy.FirstFailure == expected && !policy.TryPreparePair(25, out _, out reason)
                    && reason == RegistrationPairRefusal.Faulted && policy.FirstFailure == expected,
                    "first failure is stable and a broken run cannot silently resume");
        }
        var skipped = Policy(); var skippedContext = Prepare(skipped, 1);
        Require(skipped.DiscardPrepared(skippedContext, out reason) && reason == RegistrationPairRefusal.None
                && !skipped.IsPairComplete(skippedContext), "slot pressure may discard only an untouched whole cohort");
        var next = Prepare(skipped, 2);
        Require(next.PairId == 2, "discarded ID is consumed, never reused");
        Eye(skipped, next, ProbeEye.Left); Eye(skipped, next, ProbeEye.Right);
        skipped.Stop();
        Require(!skipped.IsPairComplete(next) && !skipped.TryPreparePair(3, out _, out reason)
                && reason == RegistrationPairRefusal.Stopped, "manual stop refuses all later output");
        var overflow = Policy(ulong.MaxValue - 1); var last = Prepare(overflow, 0);
        Require(last.PairId == ulong.MaxValue, "last representable ID is valid");
        Eye(overflow, last, ProbeEye.Left); Eye(overflow, last, ProbeEye.Right);
        Require(!overflow.TryPreparePair(1, out _, out reason) && reason == RegistrationPairRefusal.PairIdOverflow
                && overflow.LastReservedPairId == ulong.MaxValue, "counter never wraps to an invented pair");
        Require(!Policy().TryPreparePair(-1, out _, out reason) && reason == RegistrationPairRefusal.InvalidCohort, "negative engine cohort refused");
    }

    private static RegistrationSourceRecord Make(ulong pair, ProbeEye eye, RegistrationSourceBuffers buffers, RegistrationPattern? reuse = null)
    {
        var context = RegistrationSource.ExpectedContext(Session, pair, 44);
        return RegistrationSource.Stamp(RegistrationSource.PreparePattern(Session, eye, context.Phase, reuse), context, buffers);
    }

    private static int ChangedPixels(byte[] left, byte[] right, int channels)
    {
        int changed = 0;
        for (int i = 0; i < left.Length; i += channels)
            if (Enumerable.Range(0, channels).Any(c => left[i + c] != right[i + c])) changed++;
        return changed;
    }

    private static void SourceControls()
    {
        var left = RegistrationSource.CreateBuffers(); var right = RegistrationSource.CreateBuffers();
        var record = Make(1, ProbeEye.Left, left); var other = Make(1, ProbeEye.Right, right);
        Require(left.Color.Length == 184320 && left.Coverage.Length == 61440 && left.Canvas.Length == 1131072
                && left.Header.Length == 132, "all source/destination allocations have fixed small extents");
        Require(record.Schema != record.Manifest.Schema && record.Profile == RegistrationSource.Profile,
                "registration wrapper never masquerades as the old static original profile");
        record.Manifest.Check("left"); ProbeManifest.CheckPair(record.Manifest, other.Manifest);
        Require(record.Manifest.ContentId == 1 && record.PairId == 1 && record.EngineFrameCohort == 44,
                "header content ID is the genuine app-owned pair, not the engine cohort");
        Require(record.Manifest.ColorSha256 == ProbeManifest.Sha256(left.Color)
                && record.Manifest.CoverageSha256 == ProbeManifest.Sha256(left.Coverage)
                && record.Manifest.CanvasSha256 == ProbeManifest.Sha256(left.Canvas), "exact completed source digests");
        Require(record.Manifest.ColorSha256 != other.Manifest.ColorSha256 && record.Manifest.CoverageSha256 != other.Manifest.CoverageSha256,
                "left/right patterns are asymmetric even with one frozen pair ID");
        Require(new HashSet<byte>(left.Coverage).SetEquals(new byte[] { 0, 64, 128, 191, 192, 255 }), "all fractional coverage codes and overlap patch exist");
        for (int band = 0; band < 6; band++)
        {
            int x = (2 * band + 1) * 320 / 12;
            int at = (180 * 320 + x) * 3;
            Require(left.Color[at] == 32 && left.Color[at + 1] == 208 && left.Color[at + 2] == 48,
                    "identical visible green is independent of true alpha");
        }
        for (int y = 0; y < 192; y++)
            for (int x = 0; x < 320; x++)
            {
                int color = ((244 + y) * 688 + 16 + x) * 3, alpha = ((244 + y) * 688 + 352 + x) * 3;
                Require(left.Canvas[color] == left.Color[(y * 320 + x) * 3]
                        && left.Canvas[alpha] == left.Coverage[y * 320 + x]
                        && left.Canvas[alpha + 1] == left.Canvas[alpha] && left.Canvas[alpha + 2] == left.Canvas[alpha],
                        "physical atlas preserves exact logical RGB/R8 coordinates");
            }
        for (int copy = 0; copy < 3; copy++)
            for (int bit = 0; bit < 1056; bit++)
            {
                bool expected = ((left.Header[bit / 8] >> (7 - bit % 8)) & 1) != 0;
                int x = 16 + bit % 64 * 8, y = 16 + copy * 76 + bit / 64 * 4;
                Require(left.Canvas[(y * 688 + x) * 3] == (expected ? 255 : 0)
                        && left.Canvas[(y * 688 + x + 4) * 3] == (expected ? 0 : 255), "all complete complementary header copies");
            }
        var dense = new HashSet<int>();
        for (int y = 0; y < 200; y++)
            for (int x = 560; x < 680; x++)
            {
                int at = (y * 688 + x) * 3;
                dense.Add(left.Canvas[at] << 16 | left.Canvas[at + 1] << 8 | left.Canvas[at + 2]);
            }
        Require(dense.Count > 1000, "registration evidence spans former blank canvas borders");
        var codeIds = new HashSet<int>();
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 20; column++)
            {
                int code = 0;
                for (int bit = 0; bit < 8; bit++)
                    if (left.Color[((row * 16) * 320 + column * 16 + bit * 2) * 3] == 216) code |= 1 << bit;
                Require(codeIds.Add(code), "coarse coordinate cell IDs are nonrepeating");
            }
        var consecutive = RegistrationSource.CreateBuffers(); Make(2, ProbeEye.Left, consecutive);
        int movedColor = ChangedPixels(left.Color, consecutive.Color, 3), movedAlpha = ChangedPixels(left.Coverage, consecutive.Coverage, 1);
        Require(movedColor > 0 && movedColor < 6144 && movedAlpha > 0 && movedAlpha < 6144, "neighbor pairs move locally rather than flashing the whole source");
        var repeatedPhase = RegistrationSource.CreateBuffers(); Make(129, ProbeEye.Left, repeatedPhase);
        Require(left.Color.SequenceEqual(repeatedPhase.Color) && left.Coverage.SequenceEqual(repeatedPhase.Coverage)
                && !left.Header.SequenceEqual(repeatedPhase.Header) && !left.Canvas.SequenceEqual(repeatedPhase.Canvas),
                "phase reuse never reuses the pair identity or complete canvas digest");
        var pattern = RegistrationSource.PreparePattern(Session, ProbeEye.Left, 0);
        var slotColor = (byte[])left.Color.Clone();
        var reused = RegistrationSource.PreparePattern(Session, ProbeEye.Left, 1, pattern);
        Require(ReferenceEquals(pattern, reused), "Update preparation can reuse fixed CPU pattern arrays");
        Make(2, ProbeEye.Left, repeatedPhase, pattern);
        Require(consecutive.Canvas.SequenceEqual(repeatedPhase.Canvas) && left.Color.SequenceEqual(slotColor),
                "workspace reuse reproduces exact bytes without modifying previous slot destinations");
        Reject(() => RegistrationSource.Stamp(pattern, RegistrationSource.ExpectedContext(Session, 1), left), "wrong phase refuses before touching a slot");
        Reject(() => RegistrationSource.PreparePattern(Session.ToUpperInvariant(), ProbeEye.Left, 0), "noncanonical session refused");
        Reject(() => RegistrationSource.PreparePattern(new string('0', 32), ProbeEye.Left, 0), "zero session refused");
        Reject(() => RegistrationSource.PreparePattern(Session, ProbeEye.Unknown, 0), "unknown eye refused");
        Reject(() => RegistrationSource.PreparePattern(Session, ProbeEye.Left, -1), "negative phase refused");
        Reject(() => RegistrationSource.PreparePattern(Session, ProbeEye.Left, 128), "phase overflow refused");
        Reject(() => RegistrationSource.ExpectedContext(Session, 0), "zero pair ID refused");
        Require(!record.PhysicalEyeProven && !record.SourceFrameAssociationProven && !record.GpuCompletionProven
                && record.NativeFrameIdentity == null && record.NativeSpatialMapping == null && record.OpaqueFallbackRequired
                && !record.PassthroughAuthorized, "synthetic CPU source never asserts native/GPU/frame/alpha authority");
    }

    private static void Emit(string[] args)
    {
        if (args.Length != 8 || args[0] != "--emit" || args[2] != "--session" || args[4] != "--pair" || args[6] != "--eye"
            || !ulong.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out ulong pair)
            || (args[7] != "left" && args[7] != "right"))
            throw new ArgumentException("Usage: --emit DIR --session HEX --pair N --eye left|right");
        var context = RegistrationSource.ExpectedContext(args[3], pair);
        var buffers = RegistrationSource.CreateBuffers();
        var record = RegistrationSource.Stamp(RegistrationSource.PreparePattern(context.Session,
            args[7] == "left" ? ProbeEye.Left : ProbeEye.Right, context.Phase), context, buffers);
        Directory.CreateDirectory(args[1]);
        string[] names = { "color.rgb8", "coverage.r8", "canvas.rgb8", "header.bin", "registration.json" };
        byte[][] values = { buffers.Color, buffers.Coverage, buffers.Canvas, buffers.Header, record.ToJsonBytes() };
        foreach (string name in names)
            if (File.Exists(Path.Combine(args[1], name))) throw new IOException("Refusing to overwrite fixture output.");
        for (int i = 0; i < names.Length; i++)
            using (var output = new FileStream(Path.Combine(args[1], names[i]), FileMode.CreateNew, FileAccess.Write))
                output.Write(values[i], 0, values[i].Length);
        Console.WriteLine("Generated bounded registration fixture; no GPU or native association.");
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 0) { Emit(args); return 0; }
            PolicyControls(); SourceControls();
            Console.WriteLine("Registration source: " + _checks + " controls passed; pure CPU only, no Unity/GPU/native proof.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
