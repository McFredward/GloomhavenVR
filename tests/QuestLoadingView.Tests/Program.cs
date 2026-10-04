using System.Globalization;
using GloomhavenVR.Core;
using GloomhavenVR.Quest;
using UnityEngine;
using UnityEngine.UI;

static class Program
{
    static int checks;
    static void Check(bool value, string message)
    {
        checks++; if (!value) throw new InvalidOperationException(message);
    }
    static Transform Find(Transform parent, string name)
    {
        if (parent.gameObject.name == name) return parent;
        foreach (Transform child in parent.children)
        {
            Transform? found = TryFind(child, name); if (found is not null) return found;
        }
        throw new InvalidOperationException("Missing actual production child " + name);
    }
    static Transform? TryFind(Transform parent, string name)
    {
        if (parent.gameObject.name == name) return parent;
        foreach (Transform child in parent.children)
        {
            Transform? found = TryFind(child, name); if (found is not null) return found;
        }
        return null;
    }
    static string Text(QuestLoadingView view, string name) => Find(view.transform, name).GetComponent<Text>().text;
    static Camera Head(string name) => new GameObject(name).AddComponent<Camera>();
    static void Main()
    {
        Check(QuestLoadingView.Percent(5, 0) == -1, "unmeasured phase invented a percentage");
        Check(QuestLoadingView.Percent(-1, 100) == 0, "negative bytes escaped bounds");
        Check(QuestLoadingView.Percent(100, 100) == 100, "verified byte completion lost");
        Check(QuestLoadingView.Percent(long.MaxValue - 1, long.MaxValue) == 99, "floating rounding prematurely completed byte phase");
        Check(QuestLoadingView.PreparationPercent(0, 0, 42, 100) == -1, "unknown preparation plan became measurable");
        for (int step = 0; step <= 5; step++)
            Check(QuestLoadingView.PreparationPercent(step, 5, 0, -1) == step * 20, "unknown native phase changed verified step total");
        Check(QuestLoadingView.PreparationPercent(2, 5, 50, 100) == 50, "actual aggregate was not mapped into current step");
        Check(QuestLoadingView.PreparationPercent(2, 5, 100, 100) == 59, "byte packet completed unobserved gate");
        Check(QuestLoadingView.PreparationPercent(4, 5, long.MaxValue, long.MaxValue) == 99, "unobserved final gate claimed readiness");
        Check(QuestLoadingView.PreparationPercent(-9, 5, -1, -1) == 0, "negative step escaped bounds");
        Check(QuestLoadingView.PreparationPercent(999, 5, 0, -1) == 100, "observed complete plan lost completion");
        Check(QuestLoadingView.Basename(@"C:\private\owned\original.rules") == "original.rules", "Windows storage path leaked");
        Check(QuestLoadingView.Basename("StreamingAssets/Movies/Ambient/Ambient_Crypt_01.mov") == "Ambient_Crypt_01.mov", "original basename lost");
        Check(QuestLoadingView.Basename("a/line\nname.rules") == "line name.rules", "file could create extra product rows");
        Check(QuestLoadingView.Basename("") == "", "empty filename gained invented text");
        string longName = QuestLoadingView.Basename("a/" + new string('x', 54) + "😀rest.rules");
        Check(longName.Length <= 56 && longName.EndsWith("…", StringComparison.Ordinal), "filename bound missing");
        Check(!char.IsHighSurrogate(longName[^2]), "filename truncation split a surrogate pair");

        Camera head = Head("existing real head");
        QuestLoadingView view = QuestLoadingView.Create(head, 27);
        Check(view.Available, "actual owner/view did not become available");
        Check(view.transform.parent == head.transform, "view escaped existing camera owner");
        view.UpdateProgress("Checking content", "Preparation", 2, 5, 30, 100,
            "Step 3 of 5", "File 2 of 4 · 30 %", "original.rules");
        Check(Text(view, "Progress percentage") == "Preparation: 46 %", "total confused with current file");
        Check(Text(view, "Preparation step") == "Step 3 of 5", "step count disappeared");
        Check(Text(view, "Current file progress") == "File 2 of 4 · 30 %", "file index/count/progress disappeared");
        Check(Text(view, "Current file name") == "original.rules", "readable file disappeared");
        Check(Math.Abs(((RectTransform)Find(view.transform, "Progress")).anchorMax.x - .46f) < .00001f, "bar differs from displayed total");
        view.UpdateProgress("Checking next file", "Preparation", 2, 5, 10, 100,
            "Step 3 of 5", "File 3 of 4 · 0 %", "next.rules");
        Check(Text(view, "Progress percentage") == "Preparation: 46 %", "file reset rewound persistent total");
        Check(Text(view, "Current file progress") == "File 3 of 4 · 0 %", "file reset was concealed instead of distinguished");
        view.UpdateProgress("Starting the game", "Preparation", 3, 5, 0, -1, "Step 4 of 5", "", "");
        Check(Text(view, "Progress percentage") == "Preparation: 60 %", "unknown native work reset/invented total");
        Check(Text(view, "Current file progress") == "" && Text(view, "Current file name") == "", "stale file survived native phase");
        view.UpdateProgress("Starting the game", "Preparation", 5, 5, 0, -1, "Step 5 of 5", "", "");
        Check(Text(view, "Progress percentage") == "Preparation: 100 %", "observed preparation completion lost");
        view.UpdatePhase("Another operation", 0, 0);
        Check(Text(view, "Progress percentage") == "—" && Text(view, "Preparation step") == "", "reusable phase API retained previous operation");
        view.UpdateProgress("Inhalte prüfen", QuestText.Get("loadingOverall", true), 2, 5, 50, 100,
            string.Format(CultureInfo.InvariantCulture, QuestText.Get("loadingStep", true), 3, 5),
            string.Format(CultureInfo.InvariantCulture, QuestText.Get("loadingFile", true), 1, 474), "<b>original.rules");
        Check(Text(view, "Progress percentage") == "Vorbereitung: 50 %", "German preparation missing");
        Check(Text(view, "Preparation step") == "Schritt 3 von 5" && Text(view, "Current file progress") == "Datei 1 von 474", "German step/file missing");
        Check(!Find(view.transform, "Current file name").GetComponent<Text>().supportRichText, "original filename became UI markup");
        head.Alive = false;
        Check(!view.Available, "destroyed Unity head stayed available through managed reference");
        QuestLoadingView replacement = QuestLoadingView.Create(Head("recovered real head"), 27);
        replacement.UpdateProgress("Recovered", "Preparation", 2, 5, 50, 100, "Step 3 of 5", "", "");
        Check(replacement.Available && Text(replacement, "Progress percentage") == "Preparation: 50 %", "recreated existing owner lost actual progress");
        replacement.Retire(); Check(!replacement.Available, "retired artwork stayed available");

        Check(!QuestStandalonePlatform.SuppressStartupScreen("QuestOriginalStartup"), "desktop synthetic name altered flat screen policy");
        Application.platform = RuntimePlatform.Android;
        Check(!QuestStandalonePlatform.SuppressStartupScreen("QuestOriginalStartup"), "unconfigured Android altered native presentation");
        string root = Path.Combine(Path.GetTempPath(), "quest-loading-view-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            QuestStandalonePlatform.Configure(root, _ => true, () => true, () => true);
            Check(QuestStandalonePlatform.SuppressStartupScreen("QuestOriginalStartup"), "synthetic Quest scene allowed empty capture");
            foreach (string scene in new[] { "Bootstrap", "Intro", "Gloomhaven_unified", "MainMenu", "", "QuestOriginalStartupExtra" })
                Check(!QuestStandalonePlatform.SuppressStartupScreen(scene), "genuine original scene suppressed: " + scene);
            Application.platform = RuntimePlatform.WindowsPlayer;
            Check(!QuestStandalonePlatform.SuppressStartupScreen("QuestOriginalStartup"), "configured bridge changed desktop visibility");
        }
        finally { Directory.Delete(root); }
        Console.WriteLine($"Quest loading view: {checks} production checks passed; native layout/headset image unverified.");
    }
}
