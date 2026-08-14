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
            RelaunchVectors.Run(t);
            ConfigStepVectors.Run(t, repoRoot);
            ScrollTurnGateVectors.Run(t);
            EnvSoundScheduleVectors.Run(t);
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
