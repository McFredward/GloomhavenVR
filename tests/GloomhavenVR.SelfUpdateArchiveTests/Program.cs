using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using GloomhavenVR;
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
            RejectLegacyRootDesktopEntry(scratch);
            RejectEscapingEntry(scratch);
            ApplyScriptCopiesAndCleansOnlyKnownRootFiles();
            FrameRelaunchPreservesOptInAndArguments(scratch);
            FrameRelaunchRefusesUnsafeArguments();
            OrdinaryRelaunchBehaviorStaysCompatible();
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
            ["BepInEx/plugins/GloomhavenVR/FrameSetup/GloomhavenVR-Setup.desktop"] = "[Desktop Entry]",
            ["INSTALL.txt"] = "English guide",
            ["INSTALL-DEUTSCH.txt"] = "Deutsche Anleitung",
        });

        var verdict = SelfUpdateZip.Verify(archive, new FileInfo(archive).Length);
        Check(verdict.Ok, "Current release layout is accepted: " + verdict.FailedTerm);
        Check(SelfUpdateZip.AllowedRootFiles.Length == 2
            && Array.IndexOf(SelfUpdateZip.AllowedRootFiles, "INSTALL.txt") >= 0
            && Array.IndexOf(SelfUpdateZip.AllowedRootFiles, "INSTALL-DEUTSCH.txt") >= 0,
            "Root allowlist stays compatible with released updaters");
        Check(verdict.Entries.Contains("BepInEx/plugins/GloomhavenVR/FrameSetup/GloomhavenVR-Setup.desktop"),
            "Frame launcher is nested inside BepInEx");
        Check(verdict.Entries.Contains("BepInEx/plugins/GloomhavenVR/FrameSetup/install-steam-frame.sh"),
            "Nested Frame setup helper is in verified entries");

        string destination = Path.Combine(scratch, "extracted");
        Check(SelfUpdateZip.Extract(archive, destination, new int[1], out string error),
            "Current layout extracts: " + error);
        Check(File.ReadAllText(Path.Combine(destination, "BepInEx/plugins/GloomhavenVR/FrameSetup/GloomhavenVR-Setup.desktop")) == "[Desktop Entry]",
            "Nested Frame launcher survives extraction");
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
            ["unrelated.txt"] = "must not install",
        });
        var verdict = SelfUpdateZip.Verify(archive, new FileInfo(archive).Length);
        Check(!verdict.Ok && verdict.FailedTerm.Contains("unrelated.txt", StringComparison.Ordinal),
            "Unknown root files remain forbidden");
    }

    private static void RejectLegacyRootDesktopEntry(string scratch)
    {
        string archive = CreateZip(scratch, "root-desktop.zip", new Dictionary<string, string>
        {
            ["BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll"] = "plugin",
            ["BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll"] = "preloader",
            ["GloomhavenVR-Setup.desktop"] = "[Desktop Entry]",
        });
        var verdict = SelfUpdateZip.Verify(archive, new FileInfo(archive).Length);
        Check(!verdict.Ok && verdict.FailedTerm.Contains("GloomhavenVR-Setup.desktop", StringComparison.Ordinal),
            "A root desktop entry is rejected as released updaters would reject it");
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
        Check(!script.Contains("staged\\GloomhavenVR-Setup.desktop", StringComparison.Ordinal),
            "The updater never copies a desktop launcher to the archive root");
        Check(script.Contains("robocopy \"%HERE%staged\\BepInEx\" \"%ROOT%\\BepInEx\"", StringComparison.Ordinal),
            "The nested launcher is installed by the BepInEx tree copy");

        int installCheck = script.IndexOf("if errorlevel 8 goto installfailed", StringComparison.Ordinal);
        int successBranchEnd = script.IndexOf("goto relaunch", installCheck, StringComparison.Ordinal);
        int rollback = script.IndexOf(":installfailed", StringComparison.Ordinal);
        Check(installCheck >= 0 && successBranchEnd > installCheck && rollback > successBranchEnd,
            "The successful install branch precedes rollback");

        string[] legacy =
        {
            "install-steam-frame.sh", "steam-frame-config.py", "frame-boot-config.py",
            "GloomhavenVR-steam-logo.png", "GloomhavenVR-steam-icon.png",
            "GloomhavenVR-Setup.desktop",
        };
        foreach (string name in legacy)
        {
            string deletion = $"if exist \"%ROOT%\\{name}\" del /q \"%ROOT%\\{name}\"";
            int at = script.IndexOf(deletion, StringComparison.Ordinal);
            Check(at > installCheck && at < successBranchEnd,
                "Only the successful branch removes legacy root file " + name);
        }
    }

    private static void FrameRelaunchPreservesOptInAndArguments(string scratch)
    {
        string bepinex = Path.Combine(scratch, "BepInEx");
        string marker = FrameLaunchOptIn.MarkerPath(bepinex);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        Check(!FrameLaunchOptIn.MarkerExists(bepinex), "A normal install has no Frame marker");
        File.WriteAllText(marker, "");
        Check(FrameLaunchOptIn.MarkerExists(bepinex), "The installed Frame marker is detected");

        string[] argv = { "Z:\\home\\steamos\\Games\\GH.exe", "--gloomhavenvr", "-force-d3d11",
            "a value with spaces", "", "C:\\folder with spaces\\", "last-option" };
        Check(SelfUpdateApplyScript.TryBuild(123, "GH.exe", "780290", argv, "1.1.0",
            out string script, out string refusal, out string? dropped,
            frameInstallation: FrameLaunchOptIn.MarkerExists(bepinex)),
            "A marked Frame VR update builds: " + refusal);
        string expected = "start \"\" /D \"%ROOT%\" \"%ROOT%\\GH.exe\" --gloomhavenvr -force-d3d11 "
            + "\"a value with spaces\" \"\" \"C:\\folder with spaces\\\\\" last-option\r\n";
        Check(script.Contains(expected, StringComparison.Ordinal),
            "Frame restart retains the executable, cwd, opt-in, every option, empty argv and trailing backslashes");
        Check(!script.Contains("steam://", StringComparison.Ordinal) && dropped == null,
            "A marked Frame VR launch never falls back to the flat base Steam entry");
        Check(script.IndexOf(":gone", StringComparison.Ordinal)
            < script.IndexOf(expected, StringComparison.Ordinal), "The helper waits for the current game before relaunching");

        Check(SelfUpdateApplyScript.TryBuild(123, "GH.exe", "780290", new[] { "GH.exe", "--GLOOMHAVENVR" },
            "1.1.0", out script, out refusal, out dropped, frameInstallation: true),
            "The exact opt-in is case insensitive: " + refusal);
        Check(script.Contains("\"%ROOT%\\GH.exe\" --GLOOMHAVENVR\r\n", StringComparison.Ordinal),
            "Restart preserves the original argument's spelling");

        foreach (string[] missing in new[]
        {
            Array.Empty<string>(), new[] { "GH.exe" }, new[] { "GH.exe", "--gloomhavenvr-other" },
            new[] { "GH.exe", "path/--gloomhavenvr" },
        })
        {
            Check(!SelfUpdateApplyScript.TryBuild(123, "GH.exe", "780290", missing, "1.1.0",
                out script, out refusal, out dropped, frameInstallation: true),
                "A marked install refuses a relaunch without the exact VR token");
            Check(script.Length == 0 && refusal.Length > 0,
                "Missing Frame opt-in is refused before any script can be written");
        }
    }

    private static void FrameRelaunchRefusesUnsafeArguments()
    {
        foreach (string unsafeArgument in new[] { "quoted\"value", "one&two", "one|two", "<file", "file>",
            "one^two", "%PATH%", "caf\u00e9", "line\nfeed" })
        {
            Check(!SelfUpdateApplyScript.TryBuild(123, "GH.exe", "780290",
                new[] { "GH.exe", "--gloomhavenvr", unsafeArgument, "-force-d3d11" }, "1.1.0",
                out string script, out string refusal, out string? dropped, frameInstallation: true),
                "Frame refuses arguments its formatter cannot preserve safely");
            Check(script.Length == 0 && refusal.Length > 0 && dropped == unsafeArgument,
                "Unsafe Frame argv refuses the entire handover, instead of restarting flat");
        }
    }

    private static void OrdinaryRelaunchBehaviorStaysCompatible()
    {
        Check(SelfUpdateApplyScript.TryBuild(123, "GH.exe", "780290",
            new[] { "GH.exe", "--gloomhavenvr", "%PATH%" }, "1.1.0",
            out string script, out string refusal, out string? dropped),
            "An unmarked Steam install keeps its established relaunch: " + refusal);
        Check(script.Contains("start \"\" \"steam://rungameid/780290\"\r\n", StringComparison.Ordinal)
            && !script.Contains("start \"\" /D", StringComparison.Ordinal) && dropped == null,
            "A VR-looking token alone does not change ordinary Windows or Steam relaunch");

        Check(SelfUpdateApplyScript.TryBuild(123, "GH.exe", null, new[] { "GH.exe", "-force-d3d11" },
            "1.1.0", out script, out refusal, out dropped), "A non-Steam launch still builds: " + refusal);
        Check(script.Contains("start \"\" /D \"%ROOT%\" \"%ROOT%\\GH.exe\" -force-d3d11\r\n", StringComparison.Ordinal)
            && dropped == null, "Non-Steam direct launches preserve existing safe arguments");

        Check(SelfUpdateApplyScript.TryBuild(123, "GH.exe", null, new[] { "GH.exe", "safe", "%PATH%" },
            "1.1.0", out script, out refusal, out dropped), "Ordinary unsafe argv retains its warning policy: " + refusal);
        Check(script.Contains("start \"\" /D \"%ROOT%\" \"%ROOT%\\GH.exe\"\r\n", StringComparison.Ordinal)
            && dropped == "%PATH%", "Unmarked direct launches retain the existing all-or-none argument policy");
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
