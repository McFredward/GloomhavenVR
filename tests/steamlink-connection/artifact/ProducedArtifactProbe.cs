using System;
using System.IO;
using System.Linq;
using System.Reflection;

// Optional read-only invocation of a produced plugin with the actual game
// extension assembly, not a recompilation of the production class without it.
internal static class ProducedArtifactProbe
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        checks++;
        Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
        if (!condition) throw new InvalidOperationException(name);
    }

    private static int Main(string[] args)
    {
        try
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, query) =>
            {
                string path = Path.Combine(args[1], new AssemblyName(query.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Assembly plugin = Assembly.LoadFrom(args[0]);
            Type type = plugin.GetType("GloomhavenVR.Core.SteamLink.SteamLinkConnection", true);
            MethodInfo guard = type.GetMethod("LocalWindowsPath", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo cache = type.GetMethod("CacheFolder", BindingFlags.Static | BindingFlags.NonPublic);
            bool expected = args[2] == "fixed";
            string canonical = @"C:\Users\McFredward\AppData\Local";
            Check((bool)guard.Invoke(null, new object[] { canonical }) == expected, "produced guard agrees with explicit expected fixed/legacy mode");
            string actualCache = (string)cache.Invoke(null, new object[] { canonical, new Func<string>(() => canonical) });
            Check(actualCache == (expected ? canonical : ""), "produced cache result with two canonical folder sources");
            if (expected)
            {
                Check((string)cache.Invoke(null, new object[] { "", new Func<string>(() => canonical) }) == canonical, "produced known-folder fallback");
                Check((string)cache.Invoke(null, new object[] { "C:/Users/McFredward/AppData/Local/", new Func<string>(() => "") }) == canonical, "produced separator normalization");
            }
            foreach (string rejected in new[] { @"\\server\share", @"C:relative", @"C:\..\outside", @"C:\x:y", canonical + "\n" })
                Check(!(bool)guard.Invoke(null, new object[] { rejected }), "produced unsafe path remains refused");
            Assembly game = Assembly.LoadFrom(Path.Combine(args[1], "GH.Runtime.dll"));
            MethodInfo skip = game.GetType("StringExtensions", true).GetMethod("Skip", BindingFlags.Static | BindingFlags.Public);
            string gameSkip = (string)skip.Invoke(null, new object[] { canonical, 2 });
            Check(gameSkip == canonical.Substring(1) && gameSkip[0] == ':', "actual game overload retains count-minus-one disagreement");
            Check(new string(Enumerable.Skip<char>(canonical, 2).ToArray()) == canonical.Substring(2), "explicit framework overload excludes drive colon");
            Type protocol = plugin.GetType("GloomhavenVR.Net.NetProtocol", true);
            int build = Convert.ToInt32(protocol.GetField("ModBuild", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetRawConstantValue());
            if (args.Length > 3) Check(build == int.Parse(args[3]), "produced build matches requested value");
            Console.WriteLine("{\"produced_artifact_checks\":" + checks + ",\"build\":" + build
                + ",\"expected_fixed\":" + (expected ? "true" : "false") + ",\"actual_headset_query\":false}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
