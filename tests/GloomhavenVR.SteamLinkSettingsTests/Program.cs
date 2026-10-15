using System;
using System.IO;
using BepInEx.Configuration;
using GloomhavenVR;
using GloomhavenVR.Core;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Main(string[] args)
    {
        ModuleConfig.Path = System.IO.Path.Combine(args[0], "steamlink.cfg");
        bool persistedRequest = args[1] == "saved-on";
        if (persistedRequest)
        {
            var saved = new ConfigFile(ModuleConfig.Path, true);
            saved.Bind("SteamLink", "PayloadTestEnabled", false).Value = true;
            Check(File.ReadAllText(ModuleConfig.Path).Contains("PayloadTestEnabled = true"),
                "test starts from a real persisted BepInEx request");
        }

        // Execute the production binder against the real BepInEx configuration implementation.
        // The only boundary is locating its module file, avoiding the native game's global Paths.
        SteamLinkMaskConfig.SetRequested(true);
        Check(!SteamLinkMaskConfig.Requested, "unbound request cannot arm output");
        Check(!Defaults.SteamLinkPayloadTestEnabled, "new installations start with diagnostic output off");
        SteamLinkMaskConfig.Bind();
        Check(ModuleConfig.Created == 1 && ModuleConfig.Module == "steamlink", "one internal mod config is registered");
        Check(!SteamLinkMaskConfig.Requested, "first bind clears saved true before output can start");
        Check(File.ReadAllText(ModuleConfig.Path).Contains("PayloadTestEnabled = false"),
            "startup reset persists through the actual BepInEx file");
        Check(SteamLinkMaskConfig.PayloadTestEnabled.Definition.Section == "SteamLink"
            && SteamLinkMaskConfig.PayloadTestEnabled.Definition.Key == "PayloadTestEnabled",
            "VR catalog sees the stable public config identity");
        for (int i = 0; i < 16; i++)
        {
            SteamLinkMaskConfig.SetRequested(true);
            Check(SteamLinkMaskConfig.Requested, "fresh VR request can arm after stop");
            SteamLinkMaskConfig.Bind();
            Check(SteamLinkMaskConfig.Requested && ModuleConfig.Created == 1,
                "catalog rebind does not discard a fresh request or create another config");
            SteamLinkMaskConfig.SetRequested(false);
            Check(!SteamLinkMaskConfig.Requested, "VR off immediately clears requested output");
        }
        // The ordinary catalog toggle writes the same entry directly, without a second plugin.
        SteamLinkMaskConfig.PayloadTestEnabled.Value = true;
        Check(SteamLinkMaskConfig.Requested, "generic VR bool toggle reaches runtime request");
        SteamLinkMaskConfig.SetRequested(false);
        Check(!SteamLinkMaskConfig.Requested, "runtime completion resets the visible setting");
        Console.WriteLine($"Steam Link settings: {_checks} production/real-BepInEx assertions passed ({args[1]}).");
    }
}

namespace GloomhavenVR.Core
{
    internal static class ModuleConfig
    {
        internal static string Path = string.Empty;
        internal static string Module = string.Empty;
        internal static int Created;
        internal static ConfigFile Create(string module)
        {
            Created++;
            Module = module;
            return new ConfigFile(Path, true);
        }
    }
}
