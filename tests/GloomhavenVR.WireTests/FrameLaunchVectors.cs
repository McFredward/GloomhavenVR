using System;
using System.IO;
using GloomhavenVR.WorldUI;

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

        t.Case("frame-launch/desktop-render-policy");
        t.True(!FrameDesktopPolicy.MirrorLeftEye(false, false, true),
               "PC VR keeps a saved desktop mirror off choice");
        t.True(FrameDesktopPolicy.MirrorLeftEye(true, false, true),
               "PC VR keeps a saved desktop mirror on choice");
        t.True(!FrameDesktopPolicy.MirrorLeftEye(false, true, true),
               "Frame VR honors a saved off choice; platform defaults must never force it on");
        t.True(FrameDesktopPolicy.MirrorLeftEye(true, true, true),
               "Frame VR can enable the same spectator image as PC VR");
        t.True(!FrameDesktopPolicy.MirrorLeftEye(false, true, false),
               "Frame original flat launch does not force the VR mirror");
        t.True(FrameDesktopPolicy.ScrubGameCameras(true, true, false),
               "hidden flat screen skips discarded game-camera draw during VR");
        t.True(!FrameDesktopPolicy.ScrubGameCameras(true, true, true),
               "visible in-headset menu retains its captured native 2D cameras");
        t.True(!FrameDesktopPolicy.ScrubGameCameras(true, false, false),
               "flat launch keeps native desktop cameras");
        t.True(FrameDesktopPolicy.ScrubGameCameras(false, true, false),
               "Desktop mirror off still suppresses unused native drawing during VR");
        t.True(!FrameDesktopPolicy.ScrubGameCameras(false, true, true),
               "Black desktop retains actual in-headset native menu capture");
        t.True(!FrameDesktopPolicy.ScrubGameCameras(false, false, false),
               "Black desktop preference never suppresses an ordinary flat launch");
    }
}
