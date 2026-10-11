using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GloomhavenVR.Core;

internal static class Program
{
    private static int _assertions;

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _assertions++;
    }

    private static int Main(string[] args)
    {
        try
        {
            // Only the selected-language source and Pair factory are fixture boundaries.
            // Both help APIs and the entire original family/translation resolver are production code.
            Loc.CurrentLanguage = "German";
            string? german = Loc.ConfigHelpForPlayers("RenderQuality", "MsaaLevel");
            Check(german != null && german.StartsWith("Glättet", StringComparison.Ordinal),
                "German help uses the selected language");
            Loc.CurrentLanguage = "English";
            string? english = Loc.ConfigHelpForPlayers("RenderQuality", "MsaaLevel");
            Check(english != null && english.StartsWith("Smooth", StringComparison.Ordinal),
                "English help uses the selected language");
            Check(german != english, "English and German explanations remain separate");
            Loc.CurrentLanguage = "French";
            Check(Loc.ConfigHelpForPlayers("RenderQuality", "MsaaLevel") == english,
                "unsupported language returns English");
            Check(Loc.ModHelpForPlayers("h_vr_ct_anim_fx") == "Decorative dust and other card particles.",
                "unsupported heading language returns English");

            Loc.CurrentLanguage = "English";
            Check(Loc.ConfigHelpForPlayers("Cards", "FanStepDegrees_Items") ==
                  Loc.ConfigHelpForPlayers("Cards", "FanStepDegrees_*"),
                "card pile family uses the original resolver");
            foreach (string style in new[] { "Glove", "Plate", "Arcane" })
                Check(Loc.ConfigHelpForPlayers("FigureGrab", style + "HeldOffsetSide") ==
                      Loc.ConfigHelpForPlayers("FigureGrab", "*HeldOffsetSide"),
                    "hand style family uses the original resolver: " + style);
            foreach (string board in new[] { "Oak", "Steel", "Bronze" })
                Check(Loc.ConfigHelpForPlayers("Cards", "BoardTilt_" + board) ==
                      Loc.ConfigHelpForPlayers("Cards", "BoardTilt_*"),
                    "board family uses the original resolver: " + board);
            Check(Loc.ConfigHelpForPlayers("Hands", "GlovePinkyCounterAbduction") != null,
                "exact prefixed key remains reachable");
            Check(Loc.ConfigHelpForPlayers("Cards", "FanStepDegrees_Unknown") == null,
                "unknown pile suffix cannot hijack a family");
            Check(Loc.ConfigHelpForPlayers("Cards", "Unknown") == null,
                "unknown setting permits normal caller fallback");
            Check(Loc.ConfigHelpForPlayers("", "MsaaLevel") == null &&
                  Loc.ConfigHelpForPlayers("RenderQuality", "") == null,
                "empty setting addresses are safe");
            Check(Loc.ModHelpForPlayers("h_unknown") == null && Loc.ModHelpForPlayers("") == null,
                "unknown and empty heading IDs permit normal fallback");

            using (JsonDocument coverage = JsonDocument.Parse(File.ReadAllText(args[0])))
            {
                foreach (JsonElement key in coverage.RootElement.GetProperty("keys").EnumerateArray())
                {
                    string[] address = key.GetString()!.Split('/');
                    Loc.CurrentLanguage = "English";
                    string? en = Loc.ConfigHelpForPlayers(address[0], address[1]);
                    Loc.CurrentLanguage = "German";
                    string? de = Loc.ConfigHelpForPlayers(address[0], address[1]);
                    Check(!string.IsNullOrWhiteSpace(en) && !string.IsNullOrWhiteSpace(de),
                        "live key resolves both player languages: " + key.GetString());
                    Check(en != de, "German explanation is translated: " + key.GetString());
                    Loc.CurrentLanguage = "Unknown";
                    Check(Loc.ConfigHelpForPlayers(address[0], address[1]) == en,
                        "live key falls back to English: " + key.GetString());
                }
                foreach (JsonElement key in coverage.RootElement.GetProperty("hints").EnumerateArray())
                {
                    Loc.CurrentLanguage = "English";
                    string? en = Loc.ModHelpForPlayers(key.GetString()!);
                    Loc.CurrentLanguage = "German";
                    string? de = Loc.ModHelpForPlayers(key.GetString()!);
                    Check(!string.IsNullOrWhiteSpace(en) && !string.IsNullOrWhiteSpace(de),
                        "heading resolves both player languages: " + key.GetString());
                    Loc.CurrentLanguage = "Unknown";
                    Check(Loc.ModHelpForPlayers(key.GetString()!) == en,
                        "heading falls back to English: " + key.GetString());
                }
            }
            // The config-file translation path remains available independently of player help.
            Loc.CurrentLanguage = "German";
            Check(Loc.ConfigDescription("RenderQuality", "MsaaLevel") != null,
                "original config-file translation remains available");
            Loc.CurrentLanguage = "English";
            Check(Loc.ConfigDescription("RenderQuality", "MsaaLevel") == null,
                "original English config description still comes from the binding");
            Console.WriteLine("PASS player settings production lookup: " + _assertions + " assertions");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL player settings production lookup: " + error.Message);
            return 1;
        }
    }
}

namespace GloomhavenVR.Core
{
    internal static partial class Loc
    {
        internal static string CurrentLanguage { get; set; } = "English";
        // Documentation references this member; neither tested production help API calls it.
        internal static string Mod(string id) => throw new NotSupportedException(id);
        private static Dictionary<string, string> Pair(string english, string german) =>
            new(2) { ["English"] = english, ["German"] = german };
    }
}
