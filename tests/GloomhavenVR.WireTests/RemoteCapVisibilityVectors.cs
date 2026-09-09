using GloomhavenVR.Net;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace GloomhavenVR.WireTests;

internal static class RemoteCapVisibilityVectors
{
    internal static void Run(Harness t, string repoRoot)
    {
        t.Case("remote cap: other button edges cannot restart an in-flight hide");
        var cap = new RemoteCapVisibility(true);
        t.True(cap.Change(false), "first hidden request starts dissolve");
        for (int edge = 0; edge < 32; edge++)
            t.True(!cap.Change(false), "repeated hide is not a new transition even while object remains active");
        t.True(cap.Change(true), "reopen reverses the logical hide before its object deactivates");
        t.True(!cap.Change(true), "repeated visible request cannot restart appearance");
        t.True(cap.Change(false), "subsequent real hide remains observable");
        t.True(cap.Change(true), "hide and reopen can occur in the same rendered frame");

        t.Case("remote cap: independent roles retain independent visibility");
        var confirm = new RemoteCapVisibility(true);
        var skip = new RemoteCapVisibility(false);
        t.True(!confirm.Change(true), "unchanged confirm stays settled");
        t.True(skip.Change(true), "skip appears independently");
        t.True(skip.Change(false), "skip starts disappearing");
        t.True(confirm.Change(false), "confirm edge while skip dissolves is independent");
        t.True(!skip.Change(false), "confirm edge does not resample skip's shrinking geometry");

        t.Case("remote cap: production transitions use logical visibility and retain authored scale");
        string source = File.ReadAllText(Path.Combine(repoRoot, "src/GloomhavenVR/Net/Remote/RemoteBoardFurniture.cs"));
        t.True(PolicyIsBound(source), "SetShown guards actual transition through the logical policy");
        t.True(RestScaleIsStable(source), "actual dissolve never replaces the authored scale with an intermediate pose");
        t.True(!PolicyIsBound(source.Replace("if (!_visibility.Change(shown))", "if (_go.activeSelf == shown)")),
            "negative control: restoring activeSelf gate fails production binding");
        t.True(!RestScaleIsStable(source.Replace("internal void PlayDissolve()\n    {",
            "internal void PlayDissolve()\n    {\n        _shownScale = transform.localScale;")),
            "negative control: saving shrunken scale in actual dissolve fails");
        t.True(RestScaleIsStable(source.Replace("internal void PlayDissolve()\n    {",
            "internal void PlayDissolve()\n    {\n        // _shownScale = transform.localScale;")),
            "historical comment cannot masquerade as a scale write");
    }

    private static bool PolicyIsBound(string source)
    {
        string method = Method(source, "public void SetShown(bool shown, bool animate = false)");
        int guard = method.IndexOf("if (!_visibility.Change(shown))", StringComparison.Ordinal);
        int dissolve = method.IndexOf("_fx.PlayDissolve()", StringComparison.Ordinal);
        return guard >= 0 && dissolve > guard && Regex.IsMatch(method.Substring(guard),
            @"^if\s*\(!_visibility\.Change\(shown\)\)\s*return;");
    }

    private static bool RestScaleIsStable(string source)
    {
        string dissolve = Method(source, "internal void PlayDissolve()");
        return dissolve.Length > 0 && !Regex.IsMatch(dissolve, @"_shownScale\s*=");
    }

    internal static string Method(string source, string signature)
    {
        source = Regex.Replace(source, "//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/|@\"(?:[^\"]|\"\")*\"|\"(?:\\\\.|[^\"\\\\])*\"", " ");
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        int open = source.IndexOf('{', start), depth = 1, end = open + 1;
        if (open < 0) return string.Empty;
        while (end < source.Length && depth > 0)
        { if (source[end] == '{') depth++; else if (source[end] == '}') depth--; end++; }
        return source.Substring(open + 1, end - open - 2);
    }
}
