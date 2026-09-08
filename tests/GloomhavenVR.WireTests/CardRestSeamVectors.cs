using System.IO;
using System.Text.RegularExpressions;

namespace GloomhavenVR.WireTests;

internal static class CardRestSeamVectors
{
    internal static void Run(Harness t, string repoRoot)
    {
        t.Case("card-rest/native-confirmation-binding");
        // The pure ready/undo vectors cannot distinguish these two real API seams:
        // IsSelectionReady means two cards LAID; IsConfirmed means the player actually
        // locked selection using UIReadyToggle. Both return bool. Pin the binding too.
        string source = File.ReadAllText(Path.Combine(repoRoot, "src/GloomhavenVR/Cards/CardsGameApi.cs"));
        string code = Regex.Replace(source,
            "//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/|@\"(?:[^\"]|\"\")*\"|\"(?:\\\\.|[^\"\\\\])*\"", " ");
        const string signature = "internal static bool RestSelectionEditable(CardsHandUI hand)";
        int start = code.IndexOf(signature, System.StringComparison.Ordinal);
        t.True(start >= 0, "the rest eligibility seam exists");
        if (start < 0) return;
        start = code.IndexOf('{', start);
        int end = start + 1, depth = 1;
        while (end < code.Length && depth > 0)
        {
            if (code[end] == '{') depth++;
            if (code[end] == '}') depth--;
            end++;
        }
        string body = code.Substring(start, end - start);
        t.True(Regex.IsMatch(body, @"\bIsConfirmed\s*\(\s*hand\s*\)"),
            "rest availability reads the actual confirmed player toggle");
        t.True(!Regex.IsMatch(body, @"\bIsSelectionReady\s*\(\s*hand\s*\)"),
            "placing the second card alone must not hide rest controls");
        t.True(Regex.IsMatch(code, @"longRest\.IsSelectable"),
            "long rest reads its native pseudo-card selection latch");
        t.True(!Regex.IsMatch(code, @"longRest\.IsInteractable"),
            "the unused full-card-preview latch must not hide a selectable long rest");
    }
}
