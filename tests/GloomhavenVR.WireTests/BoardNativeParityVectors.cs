using System;
using System.IO;
using System.Text.RegularExpressions;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

/// <summary>Pure production geometry and source dependency guards. These cannot execute Unity
/// fitting or assert the image a headset presents.</summary>
internal static class BoardNativeParityVectors
{
    internal static void Run(Harness t, string repoRoot)
    {
        t.Case("board-parity/native-slot-depth");
        float[] ownerZ = { -0.020f, 0f, 0.004f, 0.018f };
        float[] wanted = { -0.0312f, -0.0052f, 0f, 0.0182f };
        float[] snap = { -0.0338f, -0.0078f, -0.0026f, 0.0156f };
        for (int i = 0; i < ownerZ.Length; i++)
        {
            t.True(Math.Abs(RemoteSlotGlowDepth.Wanted(ownerZ[i], 1.3f) - wanted[i]) < 0.000001f,
                "wanted rim follows owner depth through the scaled slot");
            t.True(Math.Abs(RemoteSlotGlowDepth.Snap(ownerZ[i], 1.3f) - snap[i]) < 0.000001f,
                "snap rim follows owner depth through the scaled slot");
            t.True(Math.Abs(RemoteSlotGlowDepth.Wanted(ownerZ[i], 1.3f)
                          - RemoteSlotGlowDepth.Snap(ownerZ[i], 1.3f) - 0.0026f) < 0.000001f,
                "snap stays in front without placing both rims on the card inset");
        }
        t.True(Math.Abs(RemoteSlotGlowDepth.Wanted(0.018f, 1f) - 0.014f) < 0.000001f,
            "unit slot scale is applied exactly once");
        string build = Read(repoRoot, "Cards/Tray/PlayTray.6.Build.cs");
        t.True(Regex.IsMatch(build, @"WantedGlowBaseZ\s*=\s*-0\.004f\s*;"),
            "numeric fixtures still match the actual native wanted base");
        t.True(Regex.IsMatch(build, @"SlotGlowBaseZ\s*=\s*-0\.006f\s*;"),
            "numeric fixtures still match the actual native snap base");
        string furniture = Read(repoRoot, "Net/Remote/RemoteBoardFurniture.cs");
        t.True(Regex.IsMatch(furniture, @"RemoteSlotGlowDepth\.Wanted\(tuning\.SlotOverlayOffset\.z,\s*Cards\.PlayTray\.SlotScale\)"),
            "production wanted call uses owner Z and native slot scale");
        t.True(Regex.IsMatch(furniture, @"RemoteSlotGlowDepth\.Snap\(tuning\.SlotOverlayOffset\.z,\s*Cards\.PlayTray\.SlotScale\)"),
            "production snap call uses owner Z and native slot scale");
        string board = Read(repoRoot, "Net/Remote/RemoteControlBoard.cs");
        t.True(Regex.IsMatch(board, @"new RemoteBoardFurniture\([\s\S]*?SlotAnchorBoardLocal\(0\),\s*SlotAnchorBoardLocal\(1\)"),
            "the glow constructor receives bare board-space anchors");

        t.Case("board-parity/native-disabled-hover-and-color-fade");
        t.True(RemoteDecisionMotion.HoverTarget(false, true, true, false, 1.2f) == 1.2f,
            "native disabled hover grows when the prefab permits it");
        t.True(RemoteDecisionMotion.HoverTarget(false, false, true, false, 1.2f) == 1f,
            "native disabled hover stays still when the prefab forbids it");
        t.True(RemoteDecisionMotion.HoverTarget(false, true, true, true, 1.2f) == 1.2f,
            "a disabled button never performs the press pop");
        t.True(Math.Abs(RemoteDecisionMotion.HoverTarget(true, false, true, true, 1.2f) - 1.1f) < 0.00001f,
            "offered press uses native half-pop value");
        t.True(RemoteDecisionMotion.FadeProgress(0.05f, 0.1f) == 0.5f,
            "native color fade has an intermediate frame, not a snapped state");
        t.True(RemoteDecisionMotion.FadeProgress(0.2f, 0.1f) == 1f,
            "a late frame settles without extrapolating color");
        t.True(RemoteDecisionMotion.FadeProgress(0f, 0f) == 1f,
            "zero-duration native transitions settle immediately");
        t.True(RemoteDecisionMotion.FadeProgress(-0.1f, 0.1f) == 0f,
            "a future start time cannot extrapolate before the initial color");

        t.Case("board-parity/paint-native-before-final-fit");
        string decision = Read(repoRoot, "Net/Remote/RemoteDecisionWidgets.cs");
        string body = Method(decision, "private bool RefreshCore(");
        t.True(PaintBeforeRequiredFit(body),
            "an unmeasurable retained native clone receives owner visibility before required final fit");
        string earlyReturn = body.Replace(" && _mirror.CloneOf(source) == null", "");
        t.True(!PaintBeforeRequiredFit(earlyReturn),
            "negative control: real first-fit early return fails even though later paint still exists");
        t.True(PaintBeforeRequiredFit(Code("// return Down before Apply(owner)\n" + body)),
            "a comment cannot satisfy or break call-order evidence");
        string rest = Method(decision, "private RectTransform? ResolveShortRestBox(");
        t.True(rest.Contains("manager.cardsHandPrefab") && rest.Contains("hand.shortRestPrefab")
               && rest.Contains("rest.dialogPrefab") && rest.Contains("_boundDialog = d"),
            "no-hand source fallback binds the same serialized native dialog asset");
        t.True(!Regex.IsMatch(rest, @"\b(?:Instantiate|Init|Show|AddListener|Invoke)\s*\("),
            "source resolution neither creates a game controller nor runs its callbacks");

        t.Case("board-parity/no-procedural-presentation-fallback");
        string construction = Method(board, "private void EnsureBuilt(");
        t.True(!Regex.IsMatch(construction, @"BoardVisual\.Quad\s*\("),
            "native asset failure cannot construct a procedural remote frame");
        t.True(construction.IndexOf("Cards.CardsDriver.EnsureBoardAssets()", StringComparison.Ordinal)
               < construction.IndexOf("new GameObject", StringComparison.Ordinal),
            "original single-owner asset recovery precedes remote hierarchy construction");
        t.True(Regex.IsMatch(board, @"EnsureBuilt\(\);\s*if\s*\(_root\s*==\s*null\)\s*return;"),
            "pending asset recovery returns before pose/content dereference");
        string track = Method(Read(repoRoot, "Net/Remote/RemoteInitiativeTrack.cs"), "public void Refresh(");
        string objectives = Method(Read(repoRoot, "Net/Remote/RemoteObjectivesPanel.cs"), "private void RefreshObjectives(");
        string elements = Method(Read(repoRoot, "Net/Remote/RemoteElementStrip.cs"), "public void Refresh(");
        t.True(NoComposition(track), "initiative refresh cannot substitute custom chips");
        t.True(NoComposition(objectives), "objective refresh cannot substitute custom rows");
        t.True(NoComposition(elements), "element refresh cannot demote to custom creation/element chips");
        t.True(!NoComposition(track + " RefreshFallback();"),
            "negative control: adding a reachable fallback call fails the gate");
        t.True(NoComposition(Code(track + " // RefreshFallback();")),
            "historical comments cannot be mistaken for reachable composition");
        t.True(!Regex.IsMatch(furniture, @"SetDecisionLines\([^;]*owner\.DecisionLines"),
            "decision text cannot rebuild a procedural option row");
    }

    private static bool PaintBeforeRequiredFit(string body)
    {
        int first = body.IndexOf("_mirror.Refresh(source)", StringComparison.Ordinal);
        int paint = body.IndexOf("Apply(owner,", StringComparison.Ordinal);
        int final = paint < 0 ? -1 : body.IndexOf("_mirror.Refresh(source)", paint, StringComparison.Ordinal);
        if (first < 0 || paint < first || final < paint) return false;
        string beforePaint = body.Substring(first, paint - first);
        return Regex.IsMatch(beforePaint, @"_mirror\.Refresh\(source\)\s*&&\s*_mirror\.CloneOf\(source\)\s*==\s*null")
            && beforePaint.Contains("Bind(kind)");
    }

    private static bool NoComposition(string body) => !string.IsNullOrWhiteSpace(body)
        && !Regex.IsMatch(body, @"\b(?:RefreshFallback|RefreshLegacyComposition|BuildChip)\s*\(");

    private static string Read(string root, string path) =>
        Code(File.ReadAllText(Path.Combine(root, "src/GloomhavenVR", path)));

    private static string Code(string source) => Regex.Replace(source,
        "//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/|@\"(?:[^\"]|\"\")*\"|\"(?:\\\\.|[^\"\\\\])*\"", " ");

    private static string Method(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        start = source.IndexOf('{', start);
        if (start < 0) return string.Empty;
        int end = start + 1, depth = 1;
        while (end < source.Length && depth > 0)
        {
            if (source[end] == '{') depth++;
            if (source[end] == '}') depth--;
            end++;
        }
        return source.Substring(start, end - start);
    }
}
