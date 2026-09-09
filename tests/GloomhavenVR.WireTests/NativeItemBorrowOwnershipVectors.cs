using System;
using System.IO;
using System.Text.RegularExpressions;

namespace GloomhavenVR.WireTests;

/// <summary>Source-bound ownership checks: Unity's native item recycle does not reparent the
/// card, so each temporary holder must release its game-owned child through the shared helper.
/// These guards inspect actual executable call sites; they do not simulate Unity destruction.</summary>
internal static class NativeItemBorrowOwnershipVectors
{
    internal static void Run(Harness t, string root)
    {
        string Read(string file) => Code(File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/Remote", file)));
        string source = Read("RemoteItemCardSource.cs");
        string helper = Method(source, "internal static void ReturnBorrowed(");
        t.Case("native-item-borrow/helper-releases-game-owned-child");
        t.True(SafeHelper(helper), "helper deactivates, detaches to the native pool, then invokes native recycle in that order");
        t.True(!SafeHelper(helper.Replace("card.SetActive(false);", string.Empty)),
            "negative control: removing deactivation cannot activate pooled controllers while reparenting");
        t.True(!SafeHelper(helper.Replace("card.transform.SetParent", "card.transform.SetSiblingIndex")),
            "negative control: returning to the pool list without reparenting does not release holder ownership");
        t.True(!SafeHelper("{ ObjectPool.RecycleCard(itemId, ObjectPool.ECardType.Item, card); "
            + "card.SetActive(false); card.transform.SetParent(ObjectPool.instance != null ? ObjectPool.instance.transform : null, false); }"),
            "negative control: game recycle must follow deactivation and detachment");
        t.True(!SafeHelper(Code("{ /* card.SetActive(false); card.transform.SetParent(ObjectPool.instance != null ? ObjectPool.instance.transform : null, false); */"
            + " ObjectPool.RecycleCard(itemId, ObjectPool.ECardType.Item, card); }")),
            "ownership instructions in comments cannot substitute for the actual helper writes");

        t.Case("native-item-borrow/three-actual-finally-paths");
        foreach (var path in new[] {
            (File: "RemoteItemCardSource.cs", Signature: "private static bool TryPooledClone(", Id: "ui.CardID", Card: "cardGo"),
            (File: "RemoteUseBarTooltip.cs", Signature: "private void PrepareItem(", Id: "item.ID", Card: "borrowed"),
            (File: "RemoteCardPlume.cs", Signature: "private static GameObject? CloneItemEmitter(", Id: "item.ID", Card: "card") })
        {
            string method = Method(Read(path.File), path.Signature);
            t.True(SafeReturn(method, path.Id, path.Card),
                path.File + ": actual finally returns the exact borrowed item before destroying its holder");
            string historical = Regex.Replace(method,
                @"(?:RemoteItemCardSource\.)?ReturnBorrowed\s*\(\s*" + Regex.Escape(path.Id)
                    + @"\s*,\s*" + Regex.Escape(path.Card) + @"\s*\)",
                "ObjectPool.RecycleCard(" + path.Id + ", ObjectPool.ECardType.Item, " + path.Card + ")");
            t.True(!SafeReturn(historical, path.Id, path.Card),
                path.File + ": negative control restoring historical raw recycle fails the ownership guard");
            t.True(!SafeReturn(method.Replace("Destroy(holder)", "Destroy(" + path.Card + ")"), path.Id, path.Card),
                path.File + ": a game-owned card cannot be substituted for the temporary holder's destruction target");
        }
    }

    private static bool SafeHelper(string body)
    {
        string code = Compact(body);
        int inactive = code.IndexOf("card.SetActive(false);", StringComparison.Ordinal);
        int detached = code.IndexOf("card.transform.SetParent(ObjectPool.instance!=null?ObjectPool.instance.transform:null,false);", StringComparison.Ordinal);
        int recycled = code.IndexOf("ObjectPool.RecycleCard(itemId,ObjectPool.ECardType.Item,card);", StringComparison.Ordinal);
        return inactive >= 0 && detached > inactive && recycled > detached
            && !code.Contains("Destroy(card)") && !code.Contains("card.SetActive(true)");
    }

    private static bool SafeReturn(string body, string id, string card)
    {
        string code = Compact(body);
        int finallyAt = code.IndexOf("finally{", StringComparison.Ordinal);
        int returned = code.IndexOf("ReturnBorrowed(" + id + "," + card + ")", StringComparison.Ordinal);
        int destroyed = code.IndexOf("Destroy(holder)", StringComparison.Ordinal);
        // Other failure branches may destroy an invalid borrow before the return branch; no
        // direct recycle of a valid item may bypass detachment in this method.
        return finallyAt >= 0 && returned > finallyAt && destroyed > returned
            && !code.Contains("ObjectPool.RecycleCard(");
    }

    private static string Compact(string code) => Regex.Replace(code, @"\s+", string.Empty);
    private static string Code(string source) => Regex.Replace(source,
        "@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|/\\*[\\s\\S]*?\\*/|//[^\\r\\n]*", " ");
    private static string Method(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        if (at < 0) return string.Empty;
        int start = source.IndexOf('{', at), depth = 0;
        for (int i = start; start >= 0 && i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(start, i - start + 1);
        }
        return string.Empty;
    }
}
