using System.Text.Json;

namespace QuestWeaver;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "help")
            {
                Console.WriteLine("QuestWeaver audit|weave --mod DLL --managed DIR --output PATH [--report JSON] [--diagnostic-static-subset]");
                return args.Length == 0 ? 64 : 0;
            }
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            bool subset = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--diagnostic-static-subset") { subset = true; continue; }
                if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 == args.Length)
                    throw new ArgumentException("Expected a named option and value.");
                if (!options.TryAdd(args[i], args[++i])) throw new ArgumentException("Repeated option.");
            }
            foreach (string key in options.Keys)
                if (key is not ("--mod" or "--managed" or "--output" or "--report"))
                    throw new ArgumentException("Unknown option: " + key);
            string Required(string key) => options.TryGetValue(key, out string? value)
                ? Path.GetFullPath(value) : throw new ArgumentException("Missing option: " + key);
            using var model = Discovery.Load(Required("--mod"), Required("--managed"));
            AuditReport report = model.Audit();
            if (args[0] == "audit")
            {
                WriteReport(Required("--output"), report);
                Console.WriteLine($"QuestWeaver: {report.PatchClasses} classes, {report.Hooks.Count} hooks, {report.Issues.Count} unresolved integration gates.");
                return report.Issues.Count == 0 ? 0 : 2;
            }
            if (args[0] != "weave") throw new ArgumentException("Unknown command: " + args[0]);
            string output = Required("--output");
            string reportPath = options.TryGetValue("--report", out string? rp) ? Path.GetFullPath(rp) : output + ".report.json";
            if (report.Issues.Count > 0 && !subset)
            {
                WriteReport(reportPath, report);
                Console.Error.WriteLine("QuestWeaver: conversion blocked; inspect " + reportPath);
                return 2;
            }
            new Weaver(model).Write(output, report, subset);
            WriteReport(reportPath, report);
            Console.WriteLine($"QuestWeaver: wrote {report.WovenTargets} targets to {output}; complete={report.Complete.ToString().ToLowerInvariant()}.");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("QuestWeaver: " + e.GetType().Name + ": " + e.Message);
            return 1;
        }
    }

    internal static void WriteReport(string path, AuditReport report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) + "\n");
    }
}
