using System;
using System.IO;

namespace GloomhavenVR.WireTests;

/// <summary>Pin the installation marker and exact launch opt-in shared by both boot stages.</summary>
internal static class FrameLaunchVectors
{
    internal static void Run(Harness t)
    {
        t.Case("frame-launch/opt-in");
        t.True(FrameLaunchOptIn.AllowsVr(false, null), "ordinary PC installation stays enabled");
        t.True(!FrameLaunchOptIn.AllowsVr(true, null), "unreadable command line is flat");
        t.True(!FrameLaunchOptIn.AllowsVr(true, []), "original Steam entry is flat");
        t.True(!FrameLaunchOptIn.AllowsVr(true, ["GH.exe", "--gloomhavenvr-other"]),
               "a prefixed argument must not enable VR");
        t.True(FrameLaunchOptIn.AllowsVr(true, ["GH.exe", "--gloomhavenvr"]),
               "separate VR entry enables VR");
        t.True(FrameLaunchOptIn.AllowsVr(true, ["GH.exe", "--GLOOMHAVENVR"]),
               "Steam argument case does not affect the gate");

        string root = Path.Combine(Path.GetTempPath(), "ghvr-frame-launch-" + Guid.NewGuid().ToString("N"));
        string marker = FrameLaunchOptIn.MarkerPath(root);
        try
        {
            t.True(!FrameLaunchOptIn.MarkerExists(root), "missing marker preserves existing startup");
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, "");
            t.True(FrameLaunchOptIn.MarkerExists(root), "Frame install marker is recognized");
            t.True(marker.EndsWith(Path.Combine("patchers", "GloomhavenVR", "frame-launch-opt-in.marker")),
                   "installer and both boot stages use the same marker path");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
