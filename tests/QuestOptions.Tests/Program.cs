using System;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception("FAIL " + message); }
    public static int Main(string[] args)
    {
        try
        {
            string[] rows = File.ReadAllLines(args[0]);
            Check(rows.Length > 500, "live default key inventory is substantial");
            foreach (bool quest in new[] { false, true })
            {
                QuestStandalonePlatform.Enabled = quest;
                foreach (string row in rows)
                {
                    string[] key = row.Split('\t');
                    var item = new ConfigCatalog.ConfigItem(key[0], key[1]);
                    object value = item.PersistedValue;
                    bool excluded = row == "MixedReality\tKeyColor" || row == "WorldUI\tDesktopMirrorLeftEye";
                    Check(VROptionsTab.Visible(item) == (!quest || !excluded), quest ? "Quest exact exclusions match live keys" : "desktop all live options remain visible");
                    Check(ReferenceEquals(item.PersistedValue, value), "visibility leaves persisted value untouched");
                }
                foreach (string[] key in new[]
                {
                    new[] { "MixedReality", "FuturePassthroughSetting" }, new[] { "WorldUI", "FutureWindowSetting" },
                    new[] { "FutureModSection", "NewPerformanceToggle" }, new[] { "OtherSection", "KeyColor" },
                    new[] { "OtherSection", "DesktopMirrorLeftEye" }, new[] { "MixedReality", "keycolor" }
                })
                    Check(VROptionsTab.Visible(new ConfigCatalog.ConfigItem(key[0], key[1])), "future and unrelated mod options automatically remain visible");
                var folded = new ConfigCatalog.ConfigItem("MixedReality", "Enabled");
                folded.OwnPage = true; Check(!VROptionsTab.Visible(folded), "platform policy retains own-page fold");
                folded.OwnPage = false; folded.VariantVisible = false; Check(!VROptionsTab.Visible(folded), "platform policy retains variant fold");
                folded.VariantVisible = true; folded.DependencySatisfied = false; Check(!VROptionsTab.Visible(folded), "platform policy retains dependency fold");
                folded.DependencySatisfied = true; Check(VROptionsTab.Visible(folded), "Quest native mixed-reality switch remains offered");
            }
            Console.WriteLine("PASS Quest option visibility: " + assertions + " behavioral assertions over " + rows.Length + " live default keys");
            return 0;
        }
        catch (Exception error) { Console.WriteLine(error.Message); return 1; }
    }
}
