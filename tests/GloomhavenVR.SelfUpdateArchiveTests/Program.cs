using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using GloomhavenVR.Core;

internal static class Program
{
    private static int _assertions;

    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static int Main(string[] args)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "ghvr-self-update-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            CurrentReleaseLayout(scratch);
            EarlierReleaseLayout(scratch);
            RejectUnknownRootEntry(scratch);
            RejectEscapingEntry(scratch);
            ApplyScriptCopiesAndCleansOnlyKnownRootFiles();
            if (args.Length == 1)
                VerifyRealRelease(args[0]);
            Console.WriteLine($"Self-update archive regression tests: {_assertions:N0} assertions passed.");
            return 0;
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static void CurrentReleaseLayout(string scratch)
    {
        string archive = CreateZip(scratch, "current.zip", new Dictionary<string, string>
        {
            ["BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll"] = "plugin",
            ["BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll"] = "preloader",
            ["BepInEx/plugins/GloomhavenVR/FrameSetup/install-steam-frame.sh"] = "setup",
            ["INSTALL.txt"] = "English guide",
            ["INSTALL-DEUTSCH.txt"] = "Deutsche Anleitung",
            ["GloomhavenVR-Setup.desktop"] = "[Desktop Entry]",
        });

        var verdict = SelfUpdateZip.Verify(archive, new FileInfo(archive).Length);
        Check(verdict.Ok, "Current release layout is accepted: " + verdict.FailedTerm);
        Check(verdict.Entries.Contains("GloomhavenVR-Setup.desktop"), "Root desktop launcher is in verified entries");
        Check(verdict.Entries.Contains("BepInEx/plugins/GloomhavenVR/FrameSetup/install-steam-frame.sh"),
            "Nested Frame setup helper is in verified entries");

        string destination = Path.Combine(scratch, "extracted");
        Check(SelfUpdateZip.Extract(archive, destination, new int[1], out string error),
            "Current layout extracts: " + error);
        Check(File.ReadAllText(Path.Combine(destination, "GloomhavenVR-Setup.desktop")) == "[Desktop Entry]",
            "Root desktop launcher survives extraction");
        Check(File.ReadAllText(Path.Combine(destination, "BepInEx/plugins/GloomhavenVR/FrameSetup/install-steam-frame.sh")) == "setup",
            "Nested Frame setup helper survives extraction");
    }

    private static void EarlierReleaseLayout(string scratch)
    {
        string archive = CreateZip(scratch, "earlier.zip", new Dictionary<string, string>
        {
            ["BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll"] = "plugin",
            ["BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll"] = "preloader",
            ["INSTALL.txt"] = "English guide",
            ["INSTALL-DEUTSCH.txt"] = "Deutsche Anleitung",
        });
        Check(SelfUpdateZip.Verify(archive, new FileInfo(archive).Length).Ok,
            "Older archives without the Frame launcher remain valid");
    }

    private static void RejectUnknownRootEntry(string scratch)
    {
        string archive = CreateZip(scratch, "unknown-root.zip", new Dictionary<string, string>
        {
            ["BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll"] = "plugin",
            ["BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll"] = "preloader",
            ["GloomhavenVR-Setup.desktop"] = "[Desktop Entry]",
            ["unrelated.txt"] = "must not install",
        });
        var verdict = SelfUpdateZip.Verify(archive, new FileInfo(archive).Length);
        Check(!verdict.Ok && verdict.FailedTerm.Contains("unrelated.txt", StringComparison.Ordinal),
            "Unknown root files remain forbidden");
    }

    private static void RejectEscapingEntry(string scratch)
    {
        string archive = CreateZip(scratch, "escape.zip", new Dictionary<string, string>
        {
            ["BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll"] = "plugin",
            ["BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll"] = "preloader",
            ["BepInEx/../unrelated.txt"] = "must not install",
        });
        var verdict = SelfUpdateZip.Verify(archive, new FileInfo(archive).Length);
        Check(!verdict.Ok && verdict.FailedTerm.Contains("escapes", StringComparison.Ordinal),
            "Zip-slip entries remain forbidden");
    }

    private static void ApplyScriptCopiesAndCleansOnlyKnownRootFiles()
    {
        Check(SelfUpdateApplyScript.TryBuild(123, "GH.exe", "780290", new[] { "GH.exe" }, "1.1.0",
            out string script, out string refusal, out _), "Applier script builds: " + refusal);
        const string desktopCopy = "if exist \"%HERE%staged\\GloomhavenVR-Setup.desktop\" copy /Y \"%HERE%staged\\GloomhavenVR-Setup.desktop\" \"%ROOT%\\GloomhavenVR-Setup.desktop\"";
        Check(script.Contains(desktopCopy, StringComparison.Ordinal),
            "New launcher is copied into the game root when present");

        int installCheck = script.IndexOf("if errorlevel 8 goto installfailed", StringComparison.Ordinal);
        int successfulCopy = script.IndexOf(desktopCopy, StringComparison.Ordinal);
        int successBranchEnd = script.IndexOf("goto relaunch", successfulCopy, StringComparison.Ordinal);
        int rollback = script.IndexOf(":installfailed", StringComparison.Ordinal);
        Check(installCheck >= 0 && successfulCopy > installCheck && successBranchEnd > successfulCopy && rollback > successBranchEnd,
            "Launcher copy runs only after a successful BepInEx install");

        string[] legacy =
        {
            "install-steam-frame.sh", "steam-frame-config.py", "frame-boot-config.py",
            "GloomhavenVR-steam-logo.png", "GloomhavenVR-steam-icon.png",
        };
        foreach (string name in legacy)
        {
            string deletion = $"if exist \"%ROOT%\\{name}\" del /q \"%ROOT%\\{name}\"";
            int at = script.IndexOf(deletion, StringComparison.Ordinal);
            Check(at > successfulCopy && at < successBranchEnd,
                "Only the successful branch removes legacy root file " + name);
        }
        Check(!script.Contains("del /q \"%ROOT%\\GloomhavenVR-Setup.desktop\"", StringComparison.Ordinal),
            "The root desktop launcher is retained");
    }

    private static void VerifyRealRelease(string archive)
    {
        var verdict = SelfUpdateZip.Verify(archive, new FileInfo(archive).Length);
        Check(verdict.Ok, "Actual packaged release is accepted: " + verdict.FailedTerm);
    }

    private static string CreateZip(string root, string fileName, Dictionary<string, string> entries)
    {
        string archive = Path.Combine(root, fileName);
        using var output = new FileStream(archive, FileMode.CreateNew);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            using Stream payload = zip.CreateEntry(entry.Key).Open();
            byte[] bytes = Encoding.UTF8.GetBytes(entry.Value);
            payload.Write(bytes, 0, bytes.Length);
        }
        return archive;
    }
}
