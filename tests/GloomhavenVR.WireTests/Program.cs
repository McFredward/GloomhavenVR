using System;
using System.IO;

namespace GloomhavenVR.WireTests;

internal static class Program
{
    private static int Main(string[] args)
    {
        string repoRoot = args.Length > 0 ? args[0] : FindRepoRoot();

        var t = new Harness();
        try
        {
            // Pin the two shimmed constants against the real declarations FIRST — every clamp
            // vector below is only meaningful while they agree.
            Shims.VerifyAgainstSource(repoRoot);
            GoldenVectors.Run(t);
            StoryVectors.Run(t);
            RelaunchVectors.Run(t);
            ConfigStepVectors.Run(t, repoRoot);
            ScrollTurnGateVectors.Run(t);
            LiftWedgeVectors.Run(t);
            EnvSoundScheduleVectors.Run(t);
            HauntFigureVectors.Run(t);
            HeldSizeVectors.Run(t);
            PeerBoardFadeVectors.Run(t, repoRoot);
            // The two round-card facts of the 2026-08-15 hardware session: record 14's
            // standard-action qualifier byte and record 18's slot order.
            BoardSlotVectors.Run(t);
            // Source lint, not a packet: a bundled shader looked up with a bare Shader.Find
            // resolves to null with the bundle open, and has silently cost two builds.
            BundledShaderVectors.Run(t, repoRoot);
            // Same reason, different unobservable: a card face that is a few millimetres too small
            // for its body reads as "looks a bit off" from inside a headset and as nothing at all
            // from outside one. Report 12 (2026-08-15).
            CardFaceRectVectors.Run(t, repoRoot);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("FAIL  threw: " + e);
            return 2;
        }
        return t.Report();
    }

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "GloomhavenVR.sln")))
            d = d.Parent;
        return d?.FullName ?? Directory.GetCurrentDirectory();
    }
}
